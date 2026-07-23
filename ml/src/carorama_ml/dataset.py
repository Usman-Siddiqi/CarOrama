from __future__ import annotations

import json
import math
import threading
from collections import OrderedDict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

import numpy as np
import torch
from PIL import Image
from torch.utils.data import Dataset


SUPPORTED_SCHEMA_VERSION = 2


@dataclass(frozen=True)
class DrivingSample:
    scenario_id: str
    split: str
    control_tick: int
    physics_tick: int
    camera_paths: tuple[Path, ...]
    auxiliary: tuple[float, float, float]
    target: tuple[float, float]


@dataclass(frozen=True)
class TargetStatistics:
    mean: tuple[float, float]
    standard_deviation: tuple[float, float]

    @classmethod
    def from_samples(cls, samples: Iterable[DrivingSample]) -> "TargetStatistics":
        targets = np.asarray([sample.target for sample in samples], dtype=np.float64)
        if targets.size == 0:
            raise ValueError("Cannot calculate statistics for an empty sample collection.")
        standard_deviation = np.maximum(targets.std(axis=0), 1e-3)
        # Store plain Python floats so checkpoints remain compatible with
        # PyTorch's restricted, weights-only loader. NumPy scalar objects add
        # pickle globals that the safe loader intentionally rejects.
        return cls(
            tuple(float(value) for value in targets.mean(axis=0)),
            tuple(float(value) for value in standard_deviation),
        )

    def normalize(self, target: tuple[float, float]) -> tuple[float, float]:
        return tuple(
            (value - self.mean[index]) / self.standard_deviation[index]
            for index, value in enumerate(target)
        )

    def denormalize_tensor(self, target: torch.Tensor) -> torch.Tensor:
        mean = target.new_tensor(self.mean)
        standard_deviation = target.new_tensor(self.standard_deviation)
        return target * standard_deviation + mean


def discover_samples(dataset_root: Path, split: str, cameras: tuple[str, ...]) -> list[DrivingSample]:
    dataset_root = dataset_root.resolve()
    _require_complete_dataset(dataset_root)
    split_root = dataset_root / split.lower()
    if not split_root.is_dir():
        raise FileNotFoundError(f"Dataset has no '{split.lower()}' split: {dataset_root}")

    samples: list[DrivingSample] = []
    for episode_root in sorted(path for path in split_root.iterdir() if path.is_dir()):
        if not (episode_root / "_COMPLETE").is_file():
            continue
        if not _episode_passes_quality_gate(episode_root):
            continue
        descriptor = _read_json(episode_root / "episode.json")
        scenario_id = str(descriptor["scenarioId"])
        available_cameras = {camera["id"] for camera in descriptor.get("cameras", [])}
        missing = set(cameras) - available_cameras
        if missing:
            raise ValueError(f"Episode {scenario_id} is missing calibrated cameras: {sorted(missing)}")
        samples.extend(_read_episode_samples(episode_root, split.lower(), scenario_id, cameras))
    if not samples:
        raise ValueError(f"No complete synchronized camera samples found in split '{split}'.")
    return samples


def _episode_passes_quality_gate(episode_root: Path) -> bool:
    summary_path = episode_root / "summary.json"
    if not summary_path.is_file():
        return True
    summary = _read_json(summary_path)
    metrics = summary.get("metrics", {})
    termination = summary.get("termination", {})
    return (
        termination.get("reason") == "RouteCompleted"
        and float(metrics.get("routeCompletionFraction", 0.0)) >= 0.99
        and int(metrics.get("collisionCount", 0)) == 0
        and int(metrics.get("laneDepartureCount", 0)) == 0
        and int(metrics.get("redLightViolationCount", 0)) == 0
        and int(metrics.get("stopSignViolationCount", 0)) == 0
    )


def _require_complete_dataset(dataset_root: Path) -> None:
    if not (dataset_root / "_COMPLETE").is_file():
        raise ValueError(f"Dataset is incomplete or missing: {dataset_root}")
    descriptor = _read_json(dataset_root / "dataset.json")
    if descriptor.get("schemaVersion") != SUPPORTED_SCHEMA_VERSION:
        raise ValueError(
            f"Dataset schema {descriptor.get('schemaVersion')} is unsupported; expected {SUPPORTED_SCHEMA_VERSION}."
        )


def _read_episode_samples(
    episode_root: Path,
    split: str,
    scenario_id: str,
    cameras: tuple[str, ...],
) -> list[DrivingSample]:
    result: list[DrivingSample] = []
    previous_observation: dict[str, Any] | None = None
    with (episode_root / "steps.jsonl").open("r", encoding="utf-8-sig") as stream:
        for line in stream:
            record = json.loads(line)
            if record.get("recordType") == "reset":
                previous_observation = record["observation"]
                continue
            if record.get("recordType") != "step":
                continue
            if previous_observation is None:
                raise ValueError(f"Episode {scenario_id} has a step before its reset observation.")
            frames_by_tick: dict[int, dict[str, Path]] = {}
            for frame in record.get("sensorFrames", []):
                camera_id = frame["cameraId"]
                if camera_id not in cameras:
                    continue
                relative_path = Path(frame["relativePath"])
                if relative_path.is_absolute() or ".." in relative_path.parts:
                    raise ValueError(f"Unsafe frame path in {scenario_id}: {relative_path}")
                frames_by_tick.setdefault(int(frame["physicsTick"]), {})[camera_id] = episode_root / relative_path

            for physics_tick, frame_group in sorted(frames_by_tick.items()):
                if any(camera not in frame_group for camera in cameras):
                    continue
                paths = tuple(frame_group[camera] for camera in cameras)
                missing_paths = [path for path in paths if not path.is_file()]
                if missing_paths:
                    raise FileNotFoundError(f"Indexed frames are missing in {scenario_id}: {missing_paths[:3]}")
                result.append(
                    DrivingSample(
                        scenario_id=scenario_id,
                        split=split,
                        control_tick=int(record["action"]["controlTick"]),
                        physics_tick=physics_tick,
                        camera_paths=paths,
                        auxiliary=_navigation_features(previous_observation),
                        target=(
                            float(record["action"]["steering"]),
                            float(record["action"]["longitudinalAccelerationMetersPerSecondSquared"]),
                        ),
                    )
                )
            previous_observation = record["observation"]
    return result


def _navigation_features(observation: dict[str, Any]) -> tuple[float, float, float]:
    ego = observation["egoVehicle"]
    route = observation["route"]
    position = ego["worldPosition"]
    target = route["lookaheadPoint"]
    heading = float(ego["headingRadians"])
    delta_x = float(target["x"]) - float(position["x"])
    delta_y = float(target["y"]) - float(position["y"])
    forward_x, forward_y = math.cos(heading), math.sin(heading)
    left_x, left_y = forward_y, -forward_x
    forward_metres = (delta_x * forward_x) + (delta_y * forward_y)
    left_metres = (delta_x * left_x) + (delta_y * left_y)
    # These are normalizers, not hard limits. Clipping keeps an unusual reset
    # or route point from dominating the small navigation vector.
    speed = float(ego["speedMetersPerSecond"])
    return (
        max(-1.0, min(1.0, speed / 15.0)),
        max(-1.0, min(1.0, forward_metres / 10.0)),
        max(-1.0, min(1.0, left_metres / 10.0)),
    )


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


class CarOramaDataset(Dataset[tuple[torch.Tensor, torch.Tensor, torch.Tensor, int]]):
    def __init__(
        self,
        samples: list[DrivingSample],
        target_statistics: TargetStatistics,
        image_size: tuple[int, int],
        brightness_jitter: float = 0.0,
        contrast_jitter: float = 0.0,
        image_noise_standard_deviation: float = 0.0,
    ) -> None:
        if not samples:
            raise ValueError("A dataset requires at least one sample.")
        self.samples = samples
        self.target_statistics = target_statistics
        self.image_size = image_size
        self.brightness_jitter = brightness_jitter
        self.contrast_jitter = contrast_jitter
        self.image_noise_standard_deviation = image_noise_standard_deviation
        # A small per-worker cache avoids decoding the same camera frame again when
        # weighted sampling revisits an example. Raw tensors are cloned before augmentation.
        self._image_cache: OrderedDict[Path, torch.Tensor] = OrderedDict()
        self._image_cache_limit = 256
        self._image_cache_lock = threading.Lock()

    def __len__(self) -> int:
        return len(self.samples)

    def __getitem__(self, index: int) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor, int]:
        sample = self.samples[index]
        images = torch.stack([self._load_image(path) for path in sample.camera_paths])
        auxiliary = torch.tensor(sample.auxiliary, dtype=torch.float32)
        target = torch.tensor(self.target_statistics.normalize(sample.target), dtype=torch.float32)
        return images, auxiliary, target, index

    def _load_image(self, path: Path) -> torch.Tensor:
        with self._image_cache_lock:
            cached = self._image_cache.get(path)
            if cached is not None:
                self._image_cache.move_to_end(path)
        if cached is None:
            with Image.open(path) as source:
                image = source.convert("RGB").resize(self.image_size, Image.Resampling.BILINEAR)
                array = np.asarray(image, dtype=np.float32).copy() / 255.0
            loaded = torch.from_numpy(array).permute(2, 0, 1).contiguous()
            with self._image_cache_lock:
                cached = self._image_cache.get(path)
                if cached is None:
                    cached = loaded
                    self._image_cache[path] = cached
                    if len(self._image_cache) > self._image_cache_limit:
                        self._image_cache.popitem(last=False)
                else:
                    self._image_cache.move_to_end(path)
        tensor = cached.clone()
        if self.brightness_jitter > 0.0:
            brightness = 1.0 + ((torch.rand(()) * 2.0 - 1.0) * self.brightness_jitter)
            tensor.mul_(brightness)
        if self.contrast_jitter > 0.0:
            contrast = 1.0 + ((torch.rand(()) * 2.0 - 1.0) * self.contrast_jitter)
            channel_mean = tensor.mean(dim=(1, 2), keepdim=True)
            tensor = (tensor - channel_mean).mul_(contrast).add_(channel_mean)
        if self.image_noise_standard_deviation > 0.0:
            tensor.add_(torch.randn_like(tensor) * self.image_noise_standard_deviation)
        return tensor.clamp_(0.0, 1.0).sub_(0.5).div_(0.5)