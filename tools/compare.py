"""对比一份或多份识别结果 CSV，输出正确率、混淆矩阵、逐图差异和耗时。

用法：
    python tools/compare.py output/opencv_samples.csv output/halcon_samples.csv
    python tools/compare.py output/opencv_samples.csv --report output/report.md

每份 CSV 用文件名（不含扩展名）作为算法名称。报告同时打印到终端并写入 --report。
"""

from __future__ import annotations

import argparse
import csv
import statistics
import sys
from pathlib import Path

LABELS = ["Q", "H", "K", "W", "NONE"]
# 2026-10-27 起 W 单独入库（类别码 4），只有 NONE（含找不到圆）归 NG
NG = {"NONE"}


def load(path: Path) -> dict[str, dict]:
    with path.open(encoding="utf-8-sig", newline="") as handle:
        return {row["file"]: row for row in csv.DictReader(handle)}


def sorting_target(label: str) -> str:
    """分拣意义上的类别：Q/H/K/W 各进各的列，NONE 归 NG。"""
    return "NG" if label in NG else label


def summarize(name: str, rows: dict[str, dict]) -> list[str]:
    labelled = [row for row in rows.values() if row["expected"]]
    lines = [f"## {name}", ""]
    if not labelled:
        return lines + ["（没有带类别前缀的图片）", ""]

    exact = sum(row["predicted"] == row["expected"] for row in labelled)
    sorted_ok = sum(
        sorting_target(row["predicted"]) == sorting_target(row["expected"])
        for row in labelled
    )
    found = sum(row["circle_found"] == "1" for row in rows.values())
    times = [float(row["time_ms"]) for row in rows.values() if row["time_ms"]]
    lines += [
        f"- 类别完全正确：{exact}/{len(labelled)} = {exact / len(labelled):.1%}",
        f"- 分拣正确（NONE 归 NG）：{sorted_ok}/{len(labelled)} = {sorted_ok / len(labelled):.1%}",
        f"- 找到圆：{found}/{len(rows)}",
    ]
    if times:
        lines.append(
            f"- 耗时 ms：平均 {statistics.mean(times):.1f}，最大 {max(times):.1f}"
        )
    lines += ["", "| 真实 \\ 识别 | " + " | ".join(LABELS) + " | 召回率 |", "|" + "---|" * (len(LABELS) + 2)]
    for expected in LABELS:
        group = [row for row in labelled if row["expected"] == expected]
        if not group:
            continue
        counts = [sum(row["predicted"] == label for row in group) for label in LABELS]
        recall = counts[LABELS.index(expected)] / len(group)
        lines.append(
            f"| {expected} | " + " | ".join(str(c) for c in counts) + f" | {recall:.0%} |"
        )
    return lines + [""]


def difference_table(names: list[str], tables: list[dict[str, dict]]) -> list[str]:
    files = sorted(set().union(*(table.keys() for table in tables)))
    header = "| 文件 | 真实 | " + " | ".join(names) + " |"
    if len(tables) == 2:
        header += " Δ圆心 px | ΔR px |"
    lines = ["## 逐图结果（* 表示识别错误）", "", header]
    lines.append("|" + "---|" * (header.count("|") - 1))
    for file in files:
        rows = [table.get(file) for table in tables]
        expected = next((row["expected"] for row in rows if row), "")
        cells = []
        for row in rows:
            if row is None:
                cells.append("—")
                continue
            mark = "*" if expected and row["predicted"] != expected else ""
            extra = f" ({row['all_types']})" if row["all_types"] else ""
            cells.append(f"{mark}{row['predicted']}{extra}")
        line = f"| {file} | {expected} | " + " | ".join(cells) + " |"
        if len(tables) == 2 and all(row and row["circle_found"] == "1" for row in rows):
            a, b = rows
            shift = (
                (float(a["row"]) - float(b["row"])) ** 2
                + (float(a["col"]) - float(b["col"])) ** 2
            ) ** 0.5
            line += f" {shift:.1f} | {float(a['radius']) - float(b['radius']):+.1f} |"
        elif len(tables) == 2:
            line += " — | — |"
        lines.append(line)
    return lines + [""]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("csv", nargs="+", type=Path)
    parser.add_argument("--report", type=Path, default=Path("output/compare_report.md"))
    args = parser.parse_args()

    names = [path.stem for path in args.csv]
    tables = [load(path) for path in args.csv]
    lines = ["# 识别结果对比", ""]
    for name, table in zip(names, tables):
        lines += summarize(name, table)
    lines += difference_table(names, tables)

    text = "\n".join(lines)
    sys.stdout.reconfigure(encoding="utf-8")  # Windows 控制台默认 GBK，统一用 UTF-8 输出
    print(text)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(text, encoding="utf-8")
    print(f"\nwritten {args.report}")


if __name__ == "__main__":
    main()
