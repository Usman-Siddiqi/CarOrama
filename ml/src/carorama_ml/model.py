from __future__ import annotations

import torch
from torch import nn


class CameraEncoder(nn.Module):
    def __init__(self, feature_count: int = 64) -> None:
        super().__init__()
        self.features = nn.Sequential(
            _conv(3, 24, 5, 2),
            _conv(24, 36, 5, 2),
            _conv(36, 48, 3, 2),
            _conv(48, 64, 3, 2),
            _conv(64, 96, 3, 2),
            nn.AdaptiveAvgPool2d((1, 1)),
            nn.Flatten(),
            nn.Linear(96, feature_count),
            nn.ReLU(inplace=True),
        )

    def forward(self, image: torch.Tensor) -> torch.Tensor:
        return self.features(image)


def _conv(input_channels: int, output_channels: int, kernel_size: int, stride: int) -> nn.Sequential:
    return nn.Sequential(
        nn.Conv2d(input_channels, output_channels, kernel_size, stride=stride, padding=kernel_size // 2),
        nn.BatchNorm2d(output_channels),
        nn.ReLU(inplace=True),
    )


class MultiCameraPolicy(nn.Module):
    """Small shared-encoder baseline for synchronized forward camera views."""

    def __init__(self, camera_count: int, auxiliary_feature_count: int = 3) -> None:
        super().__init__()
        if camera_count <= 0:
            raise ValueError("At least one camera is required.")
        self.camera_count = camera_count
        self.auxiliary_feature_count = auxiliary_feature_count
        camera_feature_count = 64
        self.camera_encoder = CameraEncoder(camera_feature_count)
        self.control_head = nn.Sequential(
            nn.Linear((camera_count * camera_feature_count) + auxiliary_feature_count, 256),
            nn.ReLU(inplace=True),
            nn.Dropout(0.20),
            nn.Linear(256, 128),
            nn.ReLU(inplace=True),
            nn.Linear(128, 2),
        )

    def forward(self, images: torch.Tensor, auxiliary: torch.Tensor) -> torch.Tensor:
        if images.ndim != 5 or images.shape[1] != self.camera_count:
            raise ValueError("Expected images shaped [batch, camera, channel, height, width].")
        batch_size = images.shape[0]
        encoded = self.camera_encoder(images.flatten(0, 1))
        encoded = encoded.reshape(batch_size, -1)
        return self.control_head(torch.cat((encoded, auxiliary), dim=1))