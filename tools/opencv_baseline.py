"""用比赛版 OpenCV 算法批量识别一个文件夹，输出与 HALCON 版同格式的 CSV。

用法：
    python tools/opencv_baseline.py --images data/samples --out output/opencv_samples.csv

文件名前缀（第一个下划线之前）是真实类别：Q/H/K/W/NONE，例如 Q_1.jpg、NONE_clean.png。
只读取 E:/opencv/edge-defect 下的 detector.py 和 config.json，不修改它们。
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
import time
from pathlib import Path

import cv2
import numpy as np

DEFAULT_DETECTOR_DIR = Path(r"E:/opencv/edge-defect")
IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"}
LABELS = {"Q", "H", "K", "W", "NONE"}
# 与 PLC/WPF 约定一致：1 Q，2 H，3 K，4 W，其余（NONE、找不到圆）为 5 NG
CODES = {"Q": 1, "H": 2, "K": 3, "W": 4}
FIELDS = [
    "file",
    "expected",
    "predicted",
    "code",
    "all_types",
    "circle_found",
    "row",
    "col",
    "radius",
    "time_ms",
]


def expected_label(path: Path) -> str:
    prefix = path.stem.split("_")[0].upper()
    return prefix if prefix in LABELS else ""


def read_image(path: Path) -> np.ndarray:
    data = np.fromfile(str(path), dtype=np.uint8)
    image = cv2.imdecode(data, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"cannot decode {path}")
    return image


def final_category(defects: list[dict]) -> str:
    """单缺陷前提下取主缺陷：显著度 = 面积 × 相对参考亮度的暗差。"""
    if not defects:
        return "NONE"

    def salience(defect: dict) -> float:
        features = defect["features"]
        reference = features.get("reference_intensity")
        median = features.get("component_median_intensity")
        contrast = 1.0
        if reference is not None and median is not None:
            contrast = max(1.0, float(reference) - float(median))
        return float(features["area"]) * contrast

    return max(defects, key=salience)["type"]


def load_detector(detector_dir: Path):
    sys.path.insert(0, str(detector_dir))
    import detector  # noqa: PLC0415  动态加载外部工程

    config = json.loads((detector_dir / "config.json").read_text(encoding="utf-8"))
    return detector, dict(config.get("detector", {}))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--images", required=True, type=Path, help="图片文件夹")
    parser.add_argument("--out", required=True, type=Path, help="输出 CSV")
    parser.add_argument("--detector-dir", type=Path, default=DEFAULT_DETECTOR_DIR)
    parser.add_argument(
        "--resize-width",
        type=int,
        default=0,
        help="识别前先缩放到该宽度（比赛生产流程用 640），0 表示不缩放",
    )
    args = parser.parse_args()

    detector, params = load_detector(args.detector_dir)
    paths = sorted(
        p for p in args.images.iterdir() if p.suffix.lower() in IMAGE_SUFFIXES
    )
    if not paths:
        raise SystemExit(f"no images in {args.images}")

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=FIELDS)
        writer.writeheader()
        for path in paths:
            image = read_image(path)
            if args.resize_width > 0:
                height = max(1, round(image.shape[0] * args.resize_width / image.shape[1]))
                image = cv2.resize(
                    image, (args.resize_width, height), interpolation=cv2.INTER_AREA
                )
            start = time.perf_counter()
            result = detector.detect_image(image, params)
            elapsed_ms = (time.perf_counter() - start) * 1000.0

            predicted = final_category(result["defects"]) if result["circle_found"] else "NONE"
            center = result["center"] or (0, 0)
            writer.writerow(
                {
                    "file": path.name,
                    "expected": expected_label(path),
                    "predicted": predicted,
                    "code": CODES.get(predicted, 5),
                    "all_types": "|".join(item["type"] for item in result["defects"]),
                    "circle_found": int(bool(result["circle_found"])),
                    "row": center[1],
                    "col": center[0],
                    "radius": result["radius"] or 0,
                    "time_ms": f"{elapsed_ms:.1f}",
                }
            )
            print(f"{path.name:32s} expected={expected_label(path):4s} predicted={predicted}")
    print(f"written {args.out}")


if __name__ == "__main__":
    main()
