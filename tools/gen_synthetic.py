"""生成合成回归图：白色圆盘 + 深色图案，文件名前缀为真实类别。

几何取自 E:/opencv/edge_defect_app/tests/test_detector_samples.py 的合成用例
（原用例圆心 (120,120)、半径 95），这里整体放大 1.6 倍，使圆半径约 152 px，
与 MV-CS004-10GC 原分辨率下的工件尺寸接近。

用法：
    python tools/gen_synthetic.py --out data/synthetic
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path

import cv2
import numpy as np

SIZE = 384
SCALE = 1.6
CENTER = SIZE // 2
RADIUS = int(round(95 * SCALE))
BACKGROUND = 90
DISK = 225
INK = 35
GHOST = 212  # 背面透印的浅色图案，暗差约 13，应被阈值排除


def pt(x: float, y: float) -> tuple[int, int]:
    """把原用例坐标 (以 120,120 为圆心) 映射到合成图坐标。"""
    return (
        int(round(CENTER + (x - 120) * SCALE)),
        int(round(CENTER + (y - 120) * SCALE)),
    )


def length(value: float) -> int:
    return max(1, int(round(value * SCALE)))


def blank_disk() -> np.ndarray:
    image = np.full((SIZE, SIZE), BACKGROUND, dtype=np.uint8)
    cv2.circle(image, (CENTER, CENTER), RADIUS, DISK, -1, cv2.LINE_AA)
    return image


def draw_v(image: np.ndarray, color: int) -> None:
    endpoint_radius = 95 - 9
    angle = math.radians(38)
    x = 120 + endpoint_radius * math.cos(angle)
    y_offset = endpoint_radius * math.sin(angle)
    cv2.line(image, pt(x, 120 - y_offset), pt(x - 28, 120), color, length(5), cv2.LINE_AA)
    cv2.line(image, pt(x - 28, 120), pt(x, 120 + y_offset), color, length(5), cv2.LINE_AA)


def draw_ghost_triangle(image: np.ndarray) -> None:
    triangle = np.array([pt(70, 150), pt(105, 150), pt(88, 175)], dtype=np.int32)
    cv2.polylines(image, [triangle], True, GHOST, length(2), cv2.LINE_AA)


def build_cases() -> dict[str, np.ndarray]:
    cases: dict[str, np.ndarray] = {}

    image = blank_disk()
    cases["NONE_clean"] = image

    image = blank_disk()
    cv2.line(image, pt(210, 120), pt(135, 120), INK, length(10), cv2.LINE_AA)
    cases["NONE_radial_line"] = image

    image = blank_disk()
    cv2.ellipse(image, (CENTER, CENTER), (length(92), length(92)), 0, 195, 345, INK, length(4), cv2.LINE_AA)
    cases["NONE_rim_arc"] = image

    image = blank_disk()
    draw_ghost_triangle(image)
    cases["NONE_faint_ghost"] = image

    image = blank_disk()
    draw_v(image, INK)
    cases["Q_two_contact_v"] = image

    image = blank_disk()
    triangle = np.array([pt(214, 105), pt(214, 135), pt(150, 120)], dtype=np.int32)
    cv2.fillPoly(image, [triangle], INK, cv2.LINE_AA)
    cases["Q_single_contact_notch"] = image

    image = blank_disk()
    cv2.circle(image, pt(120, 120), length(20), INK, -1, cv2.LINE_AA)
    cv2.circle(image, pt(120, 120), length(13), DISK, -1, cv2.LINE_AA)
    cases["K_ring"] = image

    image = blank_disk()
    cv2.ellipse(image, pt(100, 95), (length(26), length(14)), 30, 0, 360, INK, length(3), cv2.LINE_AA)
    cases["K_ellipse_ring"] = image

    image = blank_disk()
    cv2.circle(image, pt(120, 120), length(20), INK, -1, cv2.LINE_AA)
    cv2.circle(image, pt(120, 120), length(2), DISK, -1, cv2.LINE_AA)
    cases["W_blob_small_hole"] = image

    image = blank_disk()
    cv2.circle(image, pt(120, 120), int(round(RADIUS * 70 / 180)), 20, -1, cv2.LINE_AA)
    cases["W_large_blob"] = image

    image = blank_disk()
    cv2.line(image, pt(65, 120), pt(175, 120), INK, length(3), cv2.LINE_AA)
    cases["H_straight"] = image

    image = blank_disk()
    cv2.ellipse(image, pt(120, 120), (length(40), length(30)), 0, 20, 165, INK, length(3), cv2.LINE_AA)
    cases["H_curved"] = image

    image = blank_disk()
    cv2.ellipse(image, pt(120, 120), (length(40), length(30)), 0, 20, 165, INK, length(3), cv2.LINE_AA)
    draw_ghost_triangle(image)
    cases["H_curved_with_ghost"] = image

    return cases


def finish(image: np.ndarray, rng: np.random.Generator) -> np.ndarray:
    blurred = cv2.GaussianBlur(image, (0, 0), 0.8).astype(np.float32)
    noisy = blurred + rng.normal(0.0, 2.0, image.shape)
    return np.clip(noisy, 0, 255).astype(np.uint8)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=Path("data/synthetic"))
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(20261013)
    for name, image in build_cases().items():
        path = args.out / f"{name}.png"
        ok, encoded = cv2.imencode(".png", finish(image, rng))
        if not ok:
            raise RuntimeError(f"cannot encode {name}")
        encoded.tofile(str(path))
        print(path)


if __name__ == "__main__":
    main()
