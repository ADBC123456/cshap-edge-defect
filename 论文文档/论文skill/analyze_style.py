from __future__ import annotations

import json
import re
import statistics
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ANALYSIS = ROOT / "_analysis"
FILES = {
    "张天良": (ANALYSIS / "毕业设计：张天良.txt", 8),
    "赵洪斌": (ANALYSIS / "毕业设计：赵洪斌.txt", 9),
}

MARKERS = [
    "本文", "本研究", "本系统", "本课题", "本次", "我们", "咱们", "这套", "这一",
    "根据", "依据", "针对", "基于", "鉴于", "由于", "因此", "由此", "同时", "此外",
    "首先", "其次", "再次", "最后", "最终", "接着", "随后", "然后", "在此基础上",
    "通过", "借助", "利用", "采用", "选用", "实现", "完成", "进行", "开展", "构建",
    "设计", "采集", "设定", "转换", "验证", "测试", "对比", "分析", "保证", "确保",
    "能够", "可以", "需要", "需", "应", "主要用于", "以此", "从而", "进而", "如此",
    "显著", "明显", "大幅", "有效", "精准", "稳定", "可靠性", "奠定基础", "提供",
    "如图", "见图", "所示", "式中", "如表", "综上所述", "总的来看", "总的来说",
    "层面", "范畴", "维度", "状况", "操作", "把控", "契合", "予以", "当作", "痛点",
]


def percentile(values: list[int], p: float) -> float:
    if not values:
        return 0
    ordered = sorted(values)
    pos = (len(ordered) - 1) * p
    low = int(pos)
    high = min(low + 1, len(ordered) - 1)
    fraction = pos - low
    return round(ordered[low] * (1 - fraction) + ordered[high] * fraction, 1)


def clean_length(text: str) -> int:
    return len(re.sub(r"[\s，。；：、！？（）()《》‘’“”\[\]—–-]", "", text))


def extract_paragraphs(path: Path, body_start_page: int) -> list[str]:
    paragraphs: list[str] = []
    current = ""
    page = 0
    in_references = False

    def flush() -> None:
        nonlocal current
        value = current.strip()
        current = ""
        if clean_length(value) >= 25:
            paragraphs.append(value)

    for raw in path.read_text(encoding="utf-8").splitlines():
        page_match = re.match(r"===== PDF PAGE (\d+) =====", raw.strip())
        if page_match:
            flush()
            page = int(page_match.group(1))
            continue
        if page < body_start_page or in_references:
            continue
        stripped = raw.strip()
        if stripped == "参考文献":
            flush()
            in_references = True
            continue
        if not stripped:
            continue
        if "天津中德应用技术大学" in stripped and "本科生毕业设计" in stripped:
            continue
        if re.fullmatch(r"\d+", stripped):
            continue
        if re.match(r"^第\s*\d+\s*章", stripped):
            flush()
            continue
        if re.match(r"^\d+\.\d+(?:\.\d+)?\s*\S+", stripped) and clean_length(stripped) < 35:
            flush()
            continue
        if stripped.startswith(("图", "表")) and clean_length(stripped) < 45:
            flush()
            continue
        if "...." in stripped or clean_length(stripped) < 3:
            continue
        # In the layout extraction, new body paragraphs usually begin with two or
        # three spaces, while wrapped continuation lines begin at column zero.
        if re.match(r"^ {2,3}\S", raw):
            flush()
        current += stripped
    flush()
    return paragraphs


results: dict[str, object] = {}
for name, (path, start_page) in FILES.items():
    paragraphs = extract_paragraphs(path, start_page)
    text = "\n".join(paragraphs)
    sentences = [
        item.strip()
        for item in re.split(r"(?<=[。！？；])", text)
        if clean_length(item) >= 10
    ]
    paragraph_lengths = [clean_length(item) for item in paragraphs]
    sentence_lengths = [clean_length(item) for item in sentences]
    total = sum(paragraph_lengths)
    counts = Counter({marker: text.count(marker) for marker in MARKERS})
    per_10k = {
        marker: round(count / total * 10000, 2)
        for marker, count in counts.items()
        if count
    }
    results[name] = {
        "body_paragraphs": len(paragraphs),
        "body_sentences": len(sentences),
        "clean_characters": total,
        "paragraph_length": {
            "mean": round(statistics.mean(paragraph_lengths), 1),
            "median": statistics.median(paragraph_lengths),
            "p25": percentile(paragraph_lengths, 0.25),
            "p75": percentile(paragraph_lengths, 0.75),
            "p90": percentile(paragraph_lengths, 0.90),
        },
        "sentence_length": {
            "mean": round(statistics.mean(sentence_lengths), 1),
            "median": statistics.median(sentence_lengths),
            "p25": percentile(sentence_lengths, 0.25),
            "p75": percentile(sentence_lengths, 0.75),
            "p90": percentile(sentence_lengths, 0.90),
            "over_100": sum(value > 100 for value in sentence_lengths),
        },
        "marker_counts_per_10k_chars": dict(sorted(per_10k.items(), key=lambda item: -item[1])),
    }
    (ANALYSIS / f"{name}-body-paragraphs.txt").write_text(
        "\n\n".join(paragraphs), encoding="utf-8"
    )

(ANALYSIS / "style-metrics.json").write_text(
    json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(json.dumps(results, ensure_ascii=False, indent=2))
