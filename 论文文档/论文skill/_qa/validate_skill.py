from __future__ import annotations

import importlib.util
import json
import re
import sys
from pathlib import Path

from docx import Document

QA = Path(__file__).resolve().parent
ROOT = QA.parent
SKILL = ROOT / "electrical-thesis-writing"

required = [
    "SKILL.md",
    "README.md",
    "references/style-profile.md",
    "references/chapter-recipes.md",
    "references/sentence-bank.md",
    "references/compliance-checklist.md",
    "references/worked-examples.md",
    "assets/project-brief.md",
    "scripts/thesis_style_lint.py",
]
for relative in required:
    assert (SKILL / relative).is_file(), f"Missing {relative}"

main = (SKILL / "SKILL.md").read_text(encoding="utf-8")
assert main.startswith("---\n"), "Missing YAML front matter"
front_matter = main.split("---", 2)[1]
assert re.search(r"^name:\s*electrical-thesis-writing\s*$", front_matter, re.M)
assert re.search(r"^description:\s*\S+", front_matter, re.M)
for relative in sorted(set(re.findall(r"(?:references|assets|scripts)/[A-Za-z0-9._/-]+", main))):
    assert (SKILL / relative).exists(), f"Broken referenced path: {relative}"

reference_text = "\n".join(path.read_text(encoding="utf-8") for path in (SKILL / "references").glob("*.md"))
assert "[引用]" not in reference_text, "Bare citation placeholder remains"
assert "与本课题的【差异】不同" not in reference_text, "Malformed literature sentence remains"
assert "结果必须来自真实测试" not in reference_text, "Overly narrow abstract evidence rule remains"
assert "结果可来自可核查的设计输出" in reference_text

source = (SKILL / "scripts/thesis_style_lint.py").read_text(encoding="utf-8")
compile(source, str(SKILL / "scripts/thesis_style_lint.py"), "exec")
spec = importlib.util.spec_from_file_location("thesis_style_lint", SKILL / "scripts/thesis_style_lint.py")
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)

bad_text, bad_paragraphs = module.read_document(QA / "bad_sample.txt", "utf-8-sig")
bad_report = module.build_report(QA / "bad_sample.txt", bad_text, bad_paragraphs, 100, 320, 4)
bad_rules = {item["rule_id"] for item in bad_report["rule_findings"]}
expected_rules = {
    "editing-residue",
    "placeholders",
    "first-person",
    "colloquial",
    "ai-cliche",
    "unsupported-strong-claim",
    "nominalization",
    "connector-chain",
    "figure-reference-syntax",
    "figure-number-dot",
    "unit-case",
}
assert expected_rules <= bad_rules, f"Rules not triggered: {expected_rules - bad_rules}"
assert bad_report["issue_counts_by_severity"]["error"] >= 2
json.dumps(bad_report, ensure_ascii=False)

good_text, good_paragraphs = module.read_document(QA / "good_sample.txt", "utf-8-sig")
good_report = module.build_report(QA / "good_sample.txt", good_text, good_paragraphs, 100, 320, 4)
assert not good_report["rule_findings"], good_report["rule_findings"]
assert good_report["long_sentences_total"] == 0

single_line_fixture = QA / "single-line-fixture.txt"
single_line_fixture.write_text("第一段说明系统组成。\n第二段说明测试条件。", encoding="utf-8")
single_text, single_paragraphs = module.read_document(single_line_fixture, "utf-8-sig")
assert len(single_paragraphs) == 2, single_paragraphs
single_report = module.build_report(
    single_line_fixture, single_text, single_paragraphs, 100, 320, 4
)
assert single_report["metrics"]["paragraphs"] == 2

unit_text = "传感器输出0-10v电压信号。"
unit_report = module.build_report(QA / "unit-sample.txt", unit_text, [unit_text], 100, 320, 4)
assert "unit-case" in {item["rule_id"] for item in unit_report["rule_findings"]}

for marker in ("XXX", "【引用编号】", "[引用]"):
    placeholder_report = module.build_report(QA / "placeholder.txt", marker, [marker], 100, 320, 4)
    assert "placeholders" in {item["rule_id"] for item in placeholder_report["rule_findings"]}, marker

singular_text = "我认为该方案仍需验证。"
singular_report = module.build_report(QA / "singular.txt", singular_text, [singular_text], 100, 320, 4)
assert "first-person" in {item["rule_id"] for item in singular_report["rule_findings"]}

long_paragraph = "系统依据工艺要求完成信号采集与参数换算，" * 8 + "随后输出控制指令。"
long_report = module.build_report(
    QA / "long-sample.txt", long_paragraph, [long_paragraph], 100, 320, 4
)
assert long_report["long_sentences_total"] >= 1
repeated_text = "\n\n".join(["通过控制器完成数据采集。" for _ in range(4)])
repeated_report = module.build_report(
    QA / "repeated-sample.txt", repeated_text, [repeated_text], 100, 320, 4
)
assert repeated_report["repeated_sentence_openers"]

fixture = QA / "docx-fixture.docx"
doc = Document()
doc.add_paragraph("系统上电后首先完成状态检查，随后进入待机状态。")
doc.add_paragraph("我们这套设备用起来很方便，完全满足要求。")
doc.save(fixture)
docx_text, docx_paragraphs = module.read_document(fixture, "utf-8-sig")
assert len(docx_paragraphs) == 2
assert "系统上电" in docx_text
assert "完全满足" in docx_text

docx_report = module.build_report(fixture, docx_text, docx_paragraphs, 100, 320, 4)
docx_rules = {item["rule_id"] for item in docx_report["rule_findings"]}
assert {"first-person", "colloquial", "unsupported-strong-claim"} <= docx_rules

summary = {
    "status": "passed",
    "required_files": len(required),
    "referenced_paths_checked": len(set(re.findall(r"(?:references|assets|scripts)/[A-Za-z0-9._/-]+", main))),
    "bad_sample_rules_triggered": sorted(bad_rules),
    "good_sample_findings": len(good_report["rule_findings"]),
    "single_newline_paragraphs": len(single_paragraphs),
    "chinese_adjacent_unit_detected": True,
    "citation_placeholders_detected": ["XXX", "【引用编号】", "[引用]"],
    "singular_first_person_detected": True,
    "long_sentence_detection": long_report["long_sentences_total"],
    "repeated_opener_detection": repeated_report["repeated_sentence_openers"],
    "docx_paragraphs_read": len(docx_paragraphs),
    "docx_rules_triggered": sorted(docx_rules),
}
(QA / "validation-report.json").write_text(
    json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(json.dumps(summary, ensure_ascii=False, indent=2))
