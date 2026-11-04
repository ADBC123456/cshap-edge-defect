#!/usr/bin/env python3
"""Lightweight style-risk lint for Chinese engineering undergraduate theses.

This tool does not estimate AI authorship, verify facts, or replace human review.
TXT and Markdown use only the Python standard library. DOCX requires python-docx.
"""

from __future__ import annotations

import argparse
import json
import re
import statistics
import sys
from collections import Counter
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable


@dataclass(frozen=True)
class Rule:
    rule_id: str
    severity: str
    pattern: re.Pattern[str]
    message: str
    suggestion: str


@dataclass
class Example:
    paragraph: int
    excerpt: str


@dataclass
class RuleResult:
    rule_id: str
    severity: str
    message: str
    suggestion: str
    count: int
    examples: list[Example]


def compile_rule(rule_id: str, severity: str, pattern: str, message: str, suggestion: str) -> Rule:
    return Rule(rule_id, severity, re.compile(pattern), message, suggestion)


RULES = [
    compile_rule(
        "editing-residue",
        "error",
        r"梳理原句|第二步(进行)?改写|第一步(进行)?改写|作为(?:一个)?AI|以下是(?:润色|修改|生成)后|待优化|TODO|TBD",
        "疑似编辑指令或工作过程残留",
        "删除写作过程说明，只保留论文正文。",
    ),
    compile_rule(
        "placeholders",
        "error",
        r"X{3,}|【待补[^】]*】|\[待补[^\]]*\]|【引用编号】|\[引用\]|待补充|待填写",
        "存在未完成的占位内容",
        "用真实材料替换；无法确认时保留在待补清单，不进入提交稿。",
    ),
    compile_rule(
        "first-person",
        "warning",
        r"我们|咱们|这篇研究|本人认为|笔者认为|(?<!自)我(?=认为|觉得|的|[，。；：、！？\s])",
        "正文出现口语化或第一人称指称",
        "优先改用系统、模块、控制器等工程实体作主语；致谢不受此限。",
    ),
    compile_rule(
        "colloquial",
        "warning",
        r"这套|痛点|配上|搭了起来|搭起|用起来|这块|弄好|完成掉|一大截|冲着|稳当|理清楚|很贴合|挺|东西|点一下|把电源打开|是不是|得先|搞定|降下来|提上去",
        "出现口语或聊天式表达",
        "改为准确、直接的技术动词和中性名词，不要只换成更华丽的词。",
    ),
    compile_rule(
        "ai-cliche",
        "info",
        r"在当今[^。；\n]{0,20}时代|随着[^。；\n]{0,24}(?:飞速|迅速|不断)(?:发展|进步)|具有重要意义|奠定了?坚实(?:的)?基础|提供(?:了)?有力(?:的)?支撑|提供(?:了)?强有力(?:的)?支撑|赋能|深度融合|全面赋能|打造新一代|广阔的应用前景|高质量发展",
        "出现泛化背景或模板化评价",
        "若该句不增加技术信息则删除；否则改成具体对象、机制或证据。",
    ),
    compile_rule(
        "unsupported-strong-claim",
        "warning",
        r"显著(?:提高|提升|降低|改善|增强)|大幅(?:提高|提升|降低|改善|增强)|极大(?:提高|提升|降低|改善|增强)|完全(?:满足|达到|解决|对齐|契合|消除)|彻底(?:解决|消除)|行业领先|国际领先|非常高|精度很高|极为有效|无任何功能限制|全智能|无缝(?:对接|连接)",
        "强评价或绝对化表述需要证据与边界",
        "补充指标、数值、工况、样本和来源；证据不足时改为设计目的或限定结论。",
    ),
    compile_rule(
        "vague-performance-claim",
        "info",
        r"性能稳定|稳定可靠|精准控制|高效(?:精准|稳定)|性价比较高|性价比很高|操作(?:起来)?(?:很|非常)?方便|效果良好|较小区间|较小范围|明显提高|明显提升|明显降低|明显改善",
        "性能评价较为笼统",
        "写明控制对象、评价指标、测试条件和判定阈值，避免用模糊词代替证据。",
    ),
    compile_rule(
        "nominalization",
        "info",
        r"(?:进行|开展|实施)[^，。；\n]{0,16}(?:工作|操作|处理|解析|把控|掌控)|在[^，。；\n]{0,12}(?:层面|范畴)内|处于[^，。；\n]{0,12}状态|予以(?:分析|处理|验证|优化|完善)",
        "可能存在名词化或翻译腔",
        "能用直接动词时改为“采集数据、校准零点、验证干涉”等表达。",
    ),
    compile_rule(
        "connector-chain",
        "info",
        r"(?:以此|进而|从而)[^。；\n]{0,50}(?:以此|进而|从而)|(?:通过|借助)[^。；\n]{0,60}(?:通过|借助)",
        "一句中衔接词可能堆叠",
        "检查是否能拆成两句，并只保留真实的逻辑连接。",
    ),
    compile_rule(
        "figure-reference-syntax",
        "warning",
        r"见图\s*\d+[-.]\d+\s*所示|表格\s*\d+[-.]\d+|图\s*\d+[-.]\d+\s*所示的样",
        "图表引用句式不规范",
        "使用“如图2-1所示”或“见表2-1”，避免“见图……所示”和“表格”。",
    ),
    compile_rule(
        "figure-number-dot",
        "warning",
        r"[图表]\s*\d+\.\d+",
        "图表编号使用点号，可能与2027届模板不一致",
        "按模板改为分章短横线编号，如“图2-1”“表2-1”。",
    ),
    compile_rule(
        "unit-case",
        "warning",
        r"\d\s*(?:v|kv|kw|kpa|mpa)(?![A-Za-z])",
        "单位符号大小写可能有误",
        "核对SI单位写法，如V、kV、kW、kPa、MPa，并保持全文统一。",
    ),
]


OPENERS = [
    "综上所述",
    "在此基础上",
    "通过",
    "借助",
    "以此",
    "进而",
    "从而",
    "同时",
    "因此",
    "随着",
    "基于",
    "针对",
    "为了",
    "此外",
]


def read_document(path: Path, encoding: str) -> tuple[str, list[str]]:
    suffix = path.suffix.lower()
    if suffix == ".docx":
        try:
            from docx import Document  # type: ignore
        except ImportError as exc:
            raise RuntimeError("读取DOCX需要python-docx；也可先另存为UTF-8文本。") from exc
        document = Document(path)
        paragraphs = [paragraph.text.strip() for paragraph in document.paragraphs if paragraph.text.strip()]
        return "\n\n".join(paragraphs), paragraphs
    if suffix not in {".txt", ".md", ".markdown"}:
        raise RuntimeError("仅支持TXT、Markdown和DOCX文件。")
    text = path.read_text(encoding=encoding)
    if re.search(r"\n\s*\n", text):
        # Standard Markdown/TXT: blank lines delimit paragraphs; single newlines
        # inside a block are treated as wrapped lines.
        paragraphs = [part.strip() for part in re.split(r"\n\s*\n", text) if part.strip()]
    else:
        # Also support the common "one physical line per paragraph" TXT format.
        # Without this branch the whole file would be reported as one paragraph.
        paragraphs = [line.strip() for line in text.splitlines() if line.strip()]
    return text, paragraphs


def visible_length(text: str) -> int:
    return len(re.sub(r"[\s，。；：、！？（）()《》‘’“”\[\]【】—–-]", "", text))


def clean_excerpt(text: str, limit: int = 100) -> str:
    compact = re.sub(r"\s+", " ", text).strip()
    return compact if len(compact) <= limit else compact[: limit - 1] + "…"


def sentences_in(paragraph: str) -> Iterable[str]:
    normalized = re.sub(r"\s*\n\s*", "", paragraph)
    for match in re.finditer(r"[^。！？；]+[。！？；]?", normalized):
        sentence = match.group(0).strip()
        if visible_length(sentence) >= 8:
            yield sentence


def apply_rules(paragraphs: list[str], max_examples: int) -> list[RuleResult]:
    results: list[RuleResult] = []
    for rule in RULES:
        count = 0
        examples: list[Example] = []
        for paragraph_number, paragraph in enumerate(paragraphs, start=1):
            for match in rule.pattern.finditer(paragraph):
                count += 1
                if len(examples) < max_examples:
                    start = max(0, match.start() - 24)
                    end = min(len(paragraph), match.end() + 52)
                    examples.append(Example(paragraph_number, clean_excerpt(paragraph[start:end])))
        if count:
            results.append(
                RuleResult(
                    rule.rule_id,
                    rule.severity,
                    rule.message,
                    rule.suggestion,
                    count,
                    examples,
                )
            )
    return results


def repeated_openers(sentences: list[str]) -> list[dict[str, object]]:
    counts: Counter[str] = Counter()
    for sentence in sentences:
        for opener in OPENERS:
            if sentence.startswith(opener):
                counts[opener] += 1
                break
    total = max(1, len(sentences))
    return [
        {"opener": opener, "count": count, "ratio": round(count / total, 4)}
        for opener, count in counts.most_common()
        if count >= 4 and count / total >= 0.04
    ]


def percentile(values: list[int], fraction: float) -> float:
    if not values:
        return 0
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    low = int(position)
    high = min(low + 1, len(ordered) - 1)
    weight = position - low
    return round(ordered[low] * (1 - weight) + ordered[high] * weight, 1)


def build_report(
    path: Path,
    text: str,
    paragraphs: list[str],
    sentence_warning: int,
    paragraph_warning: int,
    max_examples: int,
) -> dict[str, object]:
    sentence_records: list[dict[str, object]] = []
    all_sentences: list[str] = []
    for paragraph_number, paragraph in enumerate(paragraphs, start=1):
        for sentence in sentences_in(paragraph):
            length = visible_length(sentence)
            all_sentences.append(sentence)
            sentence_records.append(
                {
                    "paragraph": paragraph_number,
                    "length": length,
                    "excerpt": clean_excerpt(sentence),
                }
            )
    paragraph_lengths = [visible_length(paragraph) for paragraph in paragraphs]
    sentence_lengths = [int(record["length"]) for record in sentence_records]
    rule_results = apply_rules(paragraphs, max_examples)
    long_sentences = [record for record in sentence_records if int(record["length"]) > sentence_warning]
    long_paragraphs = [
        {
            "paragraph": number,
            "length": length,
            "excerpt": clean_excerpt(paragraphs[number - 1]),
        }
        for number, length in enumerate(paragraph_lengths, start=1)
        if length > paragraph_warning
    ]
    severity_counts = Counter(result.severity for result in rule_results for _ in range(result.count))
    severity_counts["warning"] += len(long_sentences) + len(long_paragraphs)
    return {
        "file": str(path),
        "notice": "仅提示语言与格式风险，不评估AI率，不核查事实真伪；所有命中需结合语境复核。",
        "metrics": {
            "visible_characters": visible_length(text),
            "paragraphs": len(paragraphs),
            "sentences": len(all_sentences),
            "paragraph_length_median": statistics.median(paragraph_lengths) if paragraph_lengths else 0,
            "sentence_length_median": statistics.median(sentence_lengths) if sentence_lengths else 0,
            "sentence_length_p90": percentile(sentence_lengths, 0.9),
        },
        "thresholds": {
            "long_sentence": sentence_warning,
            "long_paragraph": paragraph_warning,
        },
        "issue_counts_by_severity": dict(severity_counts),
        "rule_findings": [
            {
                **{key: value for key, value in asdict(result).items() if key != "examples"},
                "examples": [asdict(example) for example in result.examples],
            }
            for result in rule_results
        ],
        "long_sentences": long_sentences[:30],
        "long_sentences_total": len(long_sentences),
        "long_paragraphs": long_paragraphs[:20],
        "long_paragraphs_total": len(long_paragraphs),
        "repeated_sentence_openers": repeated_openers(all_sentences),
    }


def print_human(report: dict[str, object]) -> None:
    metrics = report["metrics"]
    assert isinstance(metrics, dict)
    print(f"文件：{report['file']}")
    print(
        "规模："
        f"{metrics['visible_characters']}字（去空白及常用标点），"
        f"{metrics['paragraphs']}段，{metrics['sentences']}句"
    )
    print(
        f"句长中位数：{metrics['sentence_length_median']}；"
        f"P90：{metrics['sentence_length_p90']}；"
        f"段长中位数：{metrics['paragraph_length_median']}"
    )
    print(f"提示统计：{report['issue_counts_by_severity']}")
    findings = report["rule_findings"]
    assert isinstance(findings, list)
    for finding in findings:
        print(f"\n[{finding['severity']}] {finding['rule_id']} × {finding['count']}")
        print(f"  {finding['message']}；{finding['suggestion']}")
        for example in finding["examples"]:
            print(f"  段{example['paragraph']}：{example['excerpt']}")
    print(
        f"\n长句：{report['long_sentences_total']}处；"
        f"长段：{report['long_paragraphs_total']}处。"
    )
    for record in report["long_sentences"][:5]:
        print(f"  段{record['paragraph']}（{record['length']}字）：{record['excerpt']}")
    repeated = report["repeated_sentence_openers"]
    if repeated:
        print(f"重复句首：{repeated}")
    print(f"\n说明：{report['notice']}")


def main() -> int:
    parser = argparse.ArgumentParser(description="电气工程本科毕设语言与格式风险检查")
    parser.add_argument("input", type=Path, help="TXT、Markdown或DOCX文件")
    parser.add_argument("--json", dest="json_path", type=Path, help="可选：写入JSON报告")
    parser.add_argument("--encoding", default="utf-8-sig", help="文本编码，默认utf-8-sig")
    parser.add_argument("--sentence-warning", type=int, default=100, help="长句提示阈值，默认100字")
    parser.add_argument("--paragraph-warning", type=int, default=320, help="长段提示阈值，默认320字")
    parser.add_argument("--max-examples", type=int, default=4, help="每条规则最多展示的例句数")
    args = parser.parse_args()

    if not args.input.is_file():
        parser.error(f"文件不存在：{args.input}")
    if args.sentence_warning < 20 or args.paragraph_warning < 50 or args.max_examples < 1:
        parser.error("阈值或例句数量无效。")

    try:
        text, paragraphs = read_document(args.input, args.encoding)
        report = build_report(
            args.input,
            text,
            paragraphs,
            args.sentence_warning,
            args.paragraph_warning,
            args.max_examples,
        )
    except (OSError, UnicodeError, RuntimeError) as exc:
        print(f"读取或检查失败：{exc}", file=sys.stderr)
        return 1

    print_human(report)
    if args.json_path:
        args.json_path.parent.mkdir(parents=True, exist_ok=True)
        args.json_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nJSON报告：{args.json_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
