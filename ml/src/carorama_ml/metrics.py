from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import matplotlib
import numpy as np

matplotlib.use("Agg")
from matplotlib import pyplot as plt  # noqa: E402


def regression_metrics(targets: np.ndarray, predictions: np.ndarray) -> dict[str, float]:
    if targets.shape != predictions.shape or targets.ndim != 2 or targets.shape[1] != 2:
        raise ValueError("Targets and predictions must both be shaped [sample, 2].")
    error = predictions - targets
    absolute_error = np.abs(error)
    return {
        "steering_mae": float(absolute_error[:, 0].mean()),
        "steering_rmse": float(np.sqrt(np.square(error[:, 0]).mean())),
        "acceleration_mae_mps2": float(absolute_error[:, 1].mean()),
        "acceleration_rmse_mps2": float(np.sqrt(np.square(error[:, 1]).mean())),
        "steering_within_0_05_fraction": float((absolute_error[:, 0] <= 0.05).mean()),
        "acceleration_within_0_25_mps2_fraction": float((absolute_error[:, 1] <= 0.25).mean()),
    }


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def plot_history(history: list[dict[str, float]], output: Path) -> None:
    epochs = [int(row["epoch"]) for row in history]
    figure, axes = plt.subplots(1, 2, figsize=(12, 4.5))
    axes[0].plot(epochs, [row["training_loss"] for row in history], label="Training")
    axes[0].plot(epochs, [row["validation_loss"] for row in history], label="Validation")
    axes[0].set(title="Imitation loss", xlabel="Epoch", ylabel="Weighted Smooth L1")
    axes[0].grid(alpha=0.25)
    axes[0].legend()

    axes[1].plot(epochs, [row["validation_steering_mae"] for row in history], label="Steering MAE")
    axes[1].plot(
        epochs,
        [row["validation_acceleration_mae_mps2"] for row in history],
        label="Acceleration MAE (m/s²)",
    )
    axes[1].set(title="Held-out validation error", xlabel="Epoch", ylabel="Absolute error")
    axes[1].grid(alpha=0.25)
    axes[1].legend()
    figure.tight_layout()
    figure.savefig(output, dpi=150)
    plt.close(figure)


def plot_predictions(
    targets: np.ndarray,
    predictions: np.ndarray,
    output: Path,
    title: str,
    maximum_points: int = 500,
) -> None:
    count = min(len(targets), maximum_points)
    indices = np.arange(count)
    figure, axes = plt.subplots(2, 1, figsize=(13, 7), sharex=True)
    axes[0].plot(indices, targets[:count, 0], label="Teacher", linewidth=1.6)
    axes[0].plot(indices, predictions[:count, 0], label="Model", linewidth=1.2, alpha=0.85)
    axes[0].set(ylabel="Steering", title=title)
    axes[0].grid(alpha=0.25)
    axes[0].legend()
    axes[1].plot(indices, targets[:count, 1], label="Teacher", linewidth=1.6)
    axes[1].plot(indices, predictions[:count, 1], label="Model", linewidth=1.2, alpha=0.85)
    axes[1].set(xlabel="Chronological sample", ylabel="Acceleration (m/s²)")
    axes[1].grid(alpha=0.25)
    axes[1].legend()
    figure.tight_layout()
    figure.savefig(output, dpi=150)
    plt.close(figure)