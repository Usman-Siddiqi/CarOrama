from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass(frozen=True)
class TrainingConfig:
    cameras: tuple[str, ...] = ("WindshieldMain", "WindshieldWide", "FrontBumper")
    image_width: int = 160
    image_height: int = 90
    batch_size: int = 64
    epochs: int = 20
    learning_rate: float = 3e-4
    weight_decay: float = 1e-4
    steering_loss_weight: float = 2.0
    acceleration_loss_weight: float = 1.0
    steering_sample_weight: float = 4.0
    braking_sample_weight: float = 1.5
    brightness_jitter: float = 0.20
    contrast_jitter: float = 0.15
    image_noise_standard_deviation: float = 0.01
    num_workers: int = 0
    # Threaded batch loading avoids Windows multiprocessing pipe restrictions.
    loader_threads: int = 0
    random_seed: int = 20260722
    early_stopping_patience: int = 6

    def validate(self) -> None:
        if not self.cameras or len(set(self.cameras)) != len(self.cameras):
            raise ValueError("At least one unique camera is required.")
        if self.image_width <= 0 or self.image_height <= 0:
            raise ValueError("Image dimensions must be positive.")
        if self.batch_size <= 0 or self.epochs <= 0 or self.num_workers < 0 or self.loader_threads < 0:
            raise ValueError("Batch size and epochs must be positive; workers cannot be negative.")
        if self.learning_rate <= 0 or self.weight_decay < 0:
            raise ValueError("Learning rate must be positive and weight decay cannot be negative.")
        if self.early_stopping_patience <= 0:
            raise ValueError("Early-stopping patience must be positive.")
        if not 0.0 <= self.brightness_jitter <= 0.5:
            raise ValueError("Brightness jitter must be between zero and 0.5.")
        if not 0.0 <= self.contrast_jitter <= 0.5:
            raise ValueError("Contrast jitter must be between zero and 0.5.")
        if not 0.0 <= self.image_noise_standard_deviation <= 0.2:
            raise ValueError("Image noise standard deviation must be between zero and 0.2.")

    @classmethod
    def load(cls, path: Path | None) -> "TrainingConfig":
        if path is None:
            config = cls()
        else:
            raw = json.loads(path.read_text(encoding="utf-8"))
            if "cameras" in raw:
                raw["cameras"] = tuple(raw["cameras"])
            config = cls(**raw)
        config.validate()
        return config

    def write(self, path: Path) -> None:
        payload = asdict(self)
        payload["cameras"] = list(self.cameras)
        path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
