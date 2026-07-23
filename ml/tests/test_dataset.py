from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from carorama_ml.dataset import CarOramaDataset, TargetStatistics, discover_samples


CAMERAS = ("WindshieldMain", "WindshieldWide", "FrontBumper")


class DatasetTests(unittest.TestCase):
    def test_discovers_aligned_frames_and_uses_action_start_state(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            episode = root / "training" / "scenario-1"
            frames = episode / "frames"
            frames.mkdir(parents=True)
            (root / "_COMPLETE").touch()
            (episode / "_COMPLETE").touch()
            _write_json(root / "dataset.json", {"schemaVersion": 2})
            _write_json(
                episode / "episode.json",
                {
                    "scenarioId": "scenario-1",
                    "cameras": [{"id": camera} for camera in CAMERAS],
                },
            )
            references = []
            for camera in CAMERAS:
                path = frames / f"{camera}.png"
                Image.new("RGB", (16, 9), (20, 40, 60)).save(path)
                references.append(
                    {
                        "cameraId": camera,
                        "physicsTick": 4,
                        "simulationTimeSeconds": 0.033,
                        "relativePath": f"frames/{camera}.png",
                    }
                )
            reset = {
                "recordType": "reset",
                "observation": _observation(speed=3.0, lookahead_x=10.0),
            }
            step = {
                "recordType": "step",
                "action": {
                    "controlTick": 0,
                    "steering": 0.25,
                    "longitudinalAccelerationMetersPerSecondSquared": -1.5,
                },
                "observation": _observation(speed=9.0, lookahead_x=2.0),
                "sensorFrames": references,
            }
            (episode / "steps.jsonl").write_text(
                json.dumps(reset) + "\n" + json.dumps(step) + "\n",
                encoding="utf-8",
            )

            samples = discover_samples(root, "training", CAMERAS)

            self.assertEqual(1, len(samples))
            self.assertEqual((0.2, 1.0, 0.0), samples[0].auxiliary)
            self.assertEqual((0.25, -1.5), samples[0].target)
            statistics = TargetStatistics.from_samples(samples)
            dataset = CarOramaDataset(samples, statistics, (8, 4))
            images, auxiliary, target, index = dataset[0]
            self.assertEqual((3, 3, 4, 8), tuple(images.shape))
            self.assertEqual((3,), tuple(auxiliary.shape))
            self.assertEqual((2,), tuple(target.shape))
            self.assertEqual(0, index)

    def test_rejects_incomplete_dataset(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            _write_json(root / "dataset.json", {"schemaVersion": 2})
            with self.assertRaisesRegex(ValueError, "incomplete"):
                discover_samples(root, "training", CAMERAS)


def _observation(speed: float, lookahead_x: float) -> dict:
    return {
        "egoVehicle": {
            "worldPosition": {"x": 0.0, "y": 0.0},
            "headingRadians": 0.0,
            "speedMetersPerSecond": speed,
        },
        "route": {"lookaheadPoint": {"x": lookahead_x, "y": 0.0}},
    }


def _write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()