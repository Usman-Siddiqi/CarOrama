from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import torch

from carorama_ml.config import TrainingConfig
from carorama_ml.dataset import TargetStatistics
from carorama_ml.model import MultiCameraPolicy
from carorama_ml.runtime import load_checkpoint, save_checkpoint


class ModelTests(unittest.TestCase):
    def test_multi_camera_policy_shape(self) -> None:
        model = MultiCameraPolicy(camera_count=3)
        result = model(torch.zeros(2, 3, 3, 90, 160), torch.zeros(2, 3))
        self.assertEqual((2, 2), tuple(result.shape))

    def test_checkpoint_round_trip(self) -> None:
        model = MultiCameraPolicy(camera_count=3)
        config = TrainingConfig(epochs=1)
        statistics = TargetStatistics((0.1, -0.2), (0.3, 1.4))
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "model.pt"
            save_checkpoint(path, model, config, statistics, 1, 0.5, {"training": "build"})
            restored, restored_config, restored_statistics, metadata = load_checkpoint(
                path,
                torch.device("cpu"),
            )
            self.assertEqual(3, restored.camera_count)
            self.assertEqual(config.cameras, restored_config.cameras)
            self.assertEqual(statistics, restored_statistics)
            self.assertEqual(1, metadata["epoch"])


if __name__ == "__main__":
    unittest.main()