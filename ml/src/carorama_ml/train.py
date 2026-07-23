from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
from collections import Counter
import csv
import json
import platform
import shutil
import sys
import time
from pathlib import Path

import numpy as np
import torch
from torch import optim
from torch.utils.data import DataLoader, WeightedRandomSampler, default_collate

from carorama_ml.config import TrainingConfig
from carorama_ml.dataset import CarOramaDataset, TargetStatistics, discover_samples
from carorama_ml.metrics import plot_history, plot_predictions, regression_metrics, write_json
from carorama_ml.model import MultiCameraPolicy
from carorama_ml.runtime import (
    choose_device,
    evaluate_loader,
    imitation_loss,
    save_checkpoint,
    seed_everything,
)


class _ThreadedBatchLoader:
    """Batch loader that overlaps image decoding without Windows worker pipes."""

    def __init__(self, dataset, indices, batch_size: int, threads: int) -> None:
        self.dataset = dataset
        self.indices = indices
        self.batch_size = batch_size
        self.threads = threads

    def __iter__(self):
        with ThreadPoolExecutor(max_workers=self.threads) as executor:
            batch_indices = []
            for index in self.indices:
                batch_indices.append(int(index))
                if len(batch_indices) == self.batch_size:
                    samples = list(executor.map(self.dataset.__getitem__, batch_indices))
                    yield default_collate(samples)
                    batch_indices = []
            if batch_indices:
                samples = list(executor.map(self.dataset.__getitem__, batch_indices))
                yield default_collate(samples)

    def __len__(self) -> int:
        return (len(self.indices) + self.batch_size - 1) // self.batch_size


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train CarOrama's initial multi-camera imitation policy.")
    parser.add_argument("--training-dataset", type=Path, required=True)
    parser.add_argument("--validation-dataset", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--config", type=Path)
    parser.add_argument("--device", default="auto", help="auto, cpu, cuda, or a device such as cuda:0")
    parser.add_argument("--epochs", type=int, help="Override the configured epoch count.")
    parser.add_argument(
        "--init-checkpoint",
        type=Path,
        help="Initialize model weights from a previous checkpoint without overwriting it.",
    )
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    config = TrainingConfig.load(arguments.config)
    if arguments.epochs is not None:
        config = TrainingConfig(**{**config.__dict__, "epochs": arguments.epochs})
        config.validate()
    output = arguments.output.resolve()
    if output.exists():
        raise FileExistsError(f"Experiment output already exists: {output}")
    output.mkdir(parents=True)

    seed_everything(config.random_seed)
    device = choose_device(arguments.device)
    training_samples = discover_samples(arguments.training_dataset, "training", config.cameras)
    validation_samples = discover_samples(arguments.validation_dataset, "validation", config.cameras)
    statistics = TargetStatistics.from_samples(training_samples)
    training_dataset = CarOramaDataset(
        training_samples,
        statistics,
        (config.image_width, config.image_height),
        brightness_jitter=config.brightness_jitter,
        contrast_jitter=config.contrast_jitter,
        image_noise_standard_deviation=config.image_noise_standard_deviation,
    )
    validation_dataset = CarOramaDataset(
        validation_samples,
        statistics,
        (config.image_width, config.image_height),
    )
    generator = torch.Generator().manual_seed(config.random_seed)
    scenario_sample_counts = Counter(sample.scenario_id for sample in training_samples)
    sample_weights = torch.as_tensor(
        [
            (
                1.0
                + (config.steering_sample_weight * abs(sample.target[0]))
                + (config.braking_sample_weight if sample.target[1] < -0.25 else 0.0)
            )
            / scenario_sample_counts[sample.scenario_id]
            for sample in training_samples
        ],
        dtype=torch.double,
    )
    sampler = WeightedRandomSampler(
        sample_weights,
        num_samples=len(training_samples),
        replacement=True,
        generator=generator,
    )
    if config.loader_threads > 0:
        # Threads avoid Windows multiprocessing pipe failures while still overlapping
        # PNG decoding and resize work with the model's GPU batches.
        training_loader = _ThreadedBatchLoader(
            training_dataset,
            sampler,
            config.batch_size,
            config.loader_threads,
        )
        validation_loader = _ThreadedBatchLoader(
            validation_dataset,
            range(len(validation_samples)),
            config.batch_size,
            config.loader_threads,
        )
    else:
        loader_options = {
            "batch_size": config.batch_size,
            "num_workers": config.num_workers,
            "pin_memory": device.type == "cuda",
        }
        if config.num_workers > 0:
            # Keep Windows workers alive between epochs and prefetch the next batch.
            loader_options.update({"persistent_workers": True, "prefetch_factor": 2})
        training_loader = DataLoader(training_dataset, sampler=sampler, **loader_options)
        validation_loader = DataLoader(validation_dataset, shuffle=False, **loader_options)

    model = MultiCameraPolicy(len(config.cameras)).to(device)
    if arguments.init_checkpoint is not None:
        init_checkpoint = arguments.init_checkpoint.resolve()
        if not init_checkpoint.is_file():
            raise FileNotFoundError(f"Initialization checkpoint does not exist: {init_checkpoint}")
        checkpoint = torch.load(init_checkpoint, map_location=device, weights_only=True)
        checkpoint_config = checkpoint.get("config", {})
        if tuple(checkpoint_config.get("cameras", ())) != tuple(config.cameras):
            raise ValueError("Initialization checkpoint camera layout does not match training config.")
        model.load_state_dict(checkpoint["model_state_dict"])
        print(f"Initialized model weights from {init_checkpoint} (epoch {checkpoint.get('epoch', '?')}).")
    optimizer = optim.AdamW(
        model.parameters(),
        lr=config.learning_rate,
        weight_decay=config.weight_decay,
    )
    scaler = torch.amp.GradScaler("cuda", enabled=device.type == "cuda")
    scheduler = optim.lr_scheduler.ReduceLROnPlateau(optimizer, factor=0.5, patience=2)
    config.write(output / "config.json")
    dataset_builds = {
        "training": _dataset_build(arguments.training_dataset),
        "validation": _dataset_build(arguments.validation_dataset),
    }
    write_json(
        output / "run.json",
        {
            "python": sys.version,
            "platform": platform.platform(),
            "torch": torch.__version__,
            "device": str(device),
            "gpu": torch.cuda.get_device_name(device) if device.type == "cuda" else None,
            "training_dataset": str(arguments.training_dataset.resolve()),
            "validation_dataset": str(arguments.validation_dataset.resolve()),
            "dataset_builds": dataset_builds,
            "training_samples": len(training_samples),
            "validation_samples": len(validation_samples),
            "parameter_count": sum(parameter.numel() for parameter in model.parameters()),
        },
    )

    history: list[dict[str, float]] = []
    best_loss = float("inf")
    stale_epochs = 0
    start_time = time.perf_counter()
    print(
        f"Training on {device}: {len(training_samples)} train samples, "
        f"{len(validation_samples)} validation samples, "
        f"{sum(parameter.numel() for parameter in model.parameters()):,} parameters."
    )
    for epoch in range(1, config.epochs + 1):
        training_loss = _train_epoch(model, training_loader, optimizer, scaler, config, device)
        validation_loss, targets, predictions = evaluate_loader(
            model,
            validation_loader,
            statistics,
            config,
            device,
        )
        metrics = regression_metrics(targets, predictions)
        scheduler.step(validation_loss)
        row = {
            "epoch": float(epoch),
            "training_loss": training_loss,
            "validation_loss": validation_loss,
            "validation_steering_mae": metrics["steering_mae"],
            "validation_acceleration_mae_mps2": metrics["acceleration_mae_mps2"],
            "learning_rate": optimizer.param_groups[0]["lr"],
        }
        history.append(row)
        _write_history(output / "history.csv", history)
        plot_history(history, output / "progress.png")
        plot_predictions(
            targets,
            predictions,
            output / "validation-predictions.png",
            "Validation: teacher versus model",
        )
        save_checkpoint(
            output / "last.pt",
            model,
            config,
            statistics,
            epoch,
            validation_loss,
            dataset_builds,
        )
        if validation_loss < best_loss - 1e-6:
            best_loss = validation_loss
            stale_epochs = 0
            shutil.copy2(output / "last.pt", output / "best.pt")
        else:
            stale_epochs += 1
        print(
            f"Epoch {epoch:02d}: train={training_loss:.4f} val={validation_loss:.4f} "
            f"steering_mae={metrics['steering_mae']:.4f} "
            f"accel_mae={metrics['acceleration_mae_mps2']:.3f} m/s²"
        )
        if stale_epochs >= config.early_stopping_patience:
            print(f"Early stopping after {epoch} epochs.")
            break

    elapsed = time.perf_counter() - start_time
    _export_onnx(output / "best.pt", output / "policy.onnx", device)
    _export_policy_metadata(output / "best.pt", output / "policy-metadata.json", device)
    _, best_config, _, checkpoint = _load_for_summary(output / "best.pt", device)
    write_json(
        output / "summary.json",
        {
            "best_epoch": checkpoint["epoch"],
            "best_validation_loss": checkpoint["validation_loss"],
            "epochs_completed": len(history),
            "elapsed_seconds": elapsed,
            "device": str(device),
            "gpu": torch.cuda.get_device_name(device) if device.type == "cuda" else None,
            "cameras": list(best_config.cameras),
            "artifacts": {
                "checkpoint": "best.pt",
                "onnx": "policy.onnx",
                "policy_metadata": "policy-metadata.json",
                "learning_curves": "progress.png",
                "validation_predictions": "validation-predictions.png",
                "history": "history.csv",
            },
        },
    )
    print(f"Training complete. Best checkpoint and visual progress: {output}")


def _train_epoch(
    model: MultiCameraPolicy,
    loader: DataLoader,
    optimizer: optim.Optimizer,
    scaler: torch.amp.GradScaler,
    config: TrainingConfig,
    device: torch.device,
) -> float:
    model.train()
    losses: list[float] = []
    for images, auxiliary, targets, _ in loader:
        images = images.to(device, non_blocking=True)
        auxiliary = auxiliary.to(device, non_blocking=True)
        targets = targets.to(device, non_blocking=True)
        optimizer.zero_grad(set_to_none=True)
        with torch.amp.autocast("cuda", dtype=torch.float16, enabled=device.type == "cuda"):
            predictions = model(images, auxiliary)
            loss = imitation_loss(predictions, targets, config)
        scaler.scale(loss).backward()
        scaler.unscale_(optimizer)
        torch.nn.utils.clip_grad_norm_(model.parameters(), 5.0)
        scaler.step(optimizer)
        scaler.update()
        losses.append(float(loss.item()))
    return float(np.mean(losses))


def _write_history(path: Path, history: list[dict[str, float]]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(history[0]))
        writer.writeheader()
        writer.writerows(history)


def _dataset_build(root: Path) -> str:
    descriptor = json.loads((root / "dataset.json").read_text(encoding="utf-8-sig"))
    return str(descriptor["buildId"])


def _load_for_summary(path: Path, device: torch.device):
    from carorama_ml.runtime import load_checkpoint

    return load_checkpoint(path, device)


def _export_policy_metadata(checkpoint_path: Path, output: Path, device: torch.device) -> None:
    _, config, statistics, checkpoint = _load_for_summary(checkpoint_path, device)
    write_json(
        output,
        {
            "schema_version": 1,
            "checkpoint_epoch": int(checkpoint["epoch"]),
            "cameras": list(config.cameras),
            "image_width": config.image_width,
            "image_height": config.image_height,
            "target_mean": [float(value) for value in statistics.mean],
            "target_standard_deviation": [
                float(value) for value in statistics.standard_deviation
            ],
        },
    )


def _export_onnx(checkpoint_path: Path, output: Path, device: torch.device) -> None:
    model, config, _, _ = _load_for_summary(checkpoint_path, device)
    model = model.cpu()
    images = torch.zeros(
        1,
        len(config.cameras),
        3,
        config.image_height,
        config.image_width,
    )
    auxiliary = torch.zeros(1, 3)
    torch.onnx.export(
        model,
        (images, auxiliary),
        output,
        input_names=["images", "auxiliary"],
        output_names=["normalized_action"],
        dynamic_axes={
            "images": {0: "batch"},
            "auxiliary": {0: "batch"},
            "normalized_action": {0: "batch"},
        },
        opset_version=18,
        dynamo=False,
    )


if __name__ == "__main__":
    main()