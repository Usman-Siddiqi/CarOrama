from __future__ import annotations

import argparse
from pathlib import Path

import torch
from torch.utils.data import DataLoader

from carorama_ml.dataset import CarOramaDataset, discover_samples
from carorama_ml.metrics import plot_predictions, regression_metrics, write_json
from carorama_ml.runtime import choose_device, evaluate_loader, load_checkpoint, seed_everything


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Evaluate a CarOrama checkpoint offline.")
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--dataset", type=Path, required=True)
    parser.add_argument("--split", choices=("training", "validation", "test"), default="test")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--device", default="auto")
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    output = arguments.output.resolve()
    if output.exists():
        raise FileExistsError(f"Evaluation output already exists: {output}")
    output.mkdir(parents=True)
    device = choose_device(arguments.device)
    model, config, statistics, checkpoint = load_checkpoint(arguments.checkpoint, device)
    seed_everything(config.random_seed)
    samples = discover_samples(arguments.dataset, arguments.split, config.cameras)
    dataset = CarOramaDataset(
        samples,
        statistics,
        (config.image_width, config.image_height),
    )
    loader = DataLoader(
        dataset,
        batch_size=config.batch_size,
        shuffle=False,
        num_workers=config.num_workers,
        pin_memory=device.type == "cuda",
    )
    loss, targets, predictions = evaluate_loader(
        model,
        loader,
        statistics,
        config,
        device,
    )
    metrics = regression_metrics(targets, predictions)
    report = {
        "split": arguments.split,
        "sample_count": len(samples),
        "checkpoint_epoch": checkpoint["epoch"],
        "normalized_imitation_loss": loss,
        **metrics,
    }
    write_json(output / "metrics.json", report)
    plot_predictions(
        targets,
        predictions,
        output / "predictions.png",
        f"{arguments.split.title()}: teacher versus model",
        maximum_points=1000,
    )
    print(
        f"{arguments.split.title()} evaluation: steering MAE={metrics['steering_mae']:.4f}, "
        f"acceleration MAE={metrics['acceleration_mae_mps2']:.3f} m/s². "
        f"Report: {output}"
    )


if __name__ == "__main__":
    main()