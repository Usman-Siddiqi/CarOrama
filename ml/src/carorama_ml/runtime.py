from __future__ import annotations

import pickle
import random
from dataclasses import asdict
from pathlib import Path
from typing import Any

import numpy as np
import torch
from torch import nn
from torch.nn import functional as F
from torch.utils.data import DataLoader

from carorama_ml.config import TrainingConfig
from carorama_ml.dataset import TargetStatistics
from carorama_ml.model import MultiCameraPolicy


CHECKPOINT_FORMAT_VERSION = 1


def seed_everything(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)
    torch.backends.cudnn.benchmark = False
    torch.backends.cudnn.deterministic = True


def choose_device(requested: str) -> torch.device:
    if requested == "auto":
        return torch.device("cuda" if torch.cuda.is_available() else "cpu")
    device = torch.device(requested)
    if device.type == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("CUDA was requested but PyTorch cannot access an NVIDIA GPU.")
    return device


def imitation_loss(
    predictions: torch.Tensor,
    targets: torch.Tensor,
    config: TrainingConfig,
) -> torch.Tensor:
    steering = F.smooth_l1_loss(predictions[:, 0], targets[:, 0])
    acceleration = F.smooth_l1_loss(predictions[:, 1], targets[:, 1])
    return (config.steering_loss_weight * steering) + (
        config.acceleration_loss_weight * acceleration
    )


def evaluate_loader(
    model: nn.Module,
    loader: DataLoader,
    statistics: TargetStatistics,
    config: TrainingConfig,
    device: torch.device,
) -> tuple[float, np.ndarray, np.ndarray]:
    model.eval()
    losses: list[float] = []
    raw_targets: list[np.ndarray] = []
    raw_predictions: list[np.ndarray] = []
    with torch.inference_mode():
        for images, auxiliary, targets, _ in loader:
            images = images.to(device, non_blocking=True)
            auxiliary = auxiliary.to(device, non_blocking=True)
            targets = targets.to(device, non_blocking=True)
            predictions = model(images, auxiliary)
            losses.append(float(imitation_loss(predictions, targets, config).item()))
            raw_targets.append(statistics.denormalize_tensor(targets).cpu().numpy())
            raw_predictions.append(statistics.denormalize_tensor(predictions).cpu().numpy())
    return float(np.mean(losses)), np.concatenate(raw_targets), np.concatenate(raw_predictions)


def save_checkpoint(
    path: Path,
    model: MultiCameraPolicy,
    config: TrainingConfig,
    statistics: TargetStatistics,
    epoch: int,
    validation_loss: float,
    dataset_builds: dict[str, str],
) -> None:
    torch.save(
        {
            "format_version": CHECKPOINT_FORMAT_VERSION,
            "model_state_dict": model.state_dict(),
            "config": {**asdict(config), "cameras": list(config.cameras)},
            "target_statistics": {
                "mean": [float(value) for value in statistics.mean],
                "standard_deviation": [float(value) for value in statistics.standard_deviation],
            },
            "epoch": epoch,
            "validation_loss": float(validation_loss),
            "dataset_builds": dataset_builds,
            "torch_version": str(torch.__version__),
        },
        path,
    )


def load_checkpoint(path: Path, device: torch.device) -> tuple[MultiCameraPolicy, TrainingConfig, TargetStatistics, dict[str, Any]]:
    try:
        checkpoint = torch.load(path, map_location=device, weights_only=True)
    except pickle.UnpicklingError:
        # Pilot checkpoints written before target statistics were converted to
        # Python floats contain only these NumPy scalar types in addition to
        # tensors and primitive values. Keep weights_only enabled and narrowly
        # allowlist those known legacy representations.
        legacy_safe_globals = [
            np.dtype,
            np._core.multiarray.scalar,
            type(np.dtype(np.float64)),
        ]
        with torch.serialization.safe_globals(legacy_safe_globals):
            checkpoint = torch.load(path, map_location=device, weights_only=True)
    if checkpoint.get("format_version") != CHECKPOINT_FORMAT_VERSION:
        raise ValueError(f"Unsupported checkpoint format: {checkpoint.get('format_version')}")
    raw_config = dict(checkpoint["config"])
    raw_config["cameras"] = tuple(raw_config["cameras"])
    config = TrainingConfig(**raw_config)
    raw_statistics = checkpoint["target_statistics"]
    statistics = TargetStatistics(
        tuple(raw_statistics["mean"]),
        tuple(raw_statistics["standard_deviation"]),
    )
    model = MultiCameraPolicy(len(config.cameras)).to(device)
    model.load_state_dict(checkpoint["model_state_dict"])
    model.eval()
    return model, config, statistics, checkpoint