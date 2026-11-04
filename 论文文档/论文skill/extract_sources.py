from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / ".tools"))

from docx import Document
from pypdf import PdfReader

OUT = ROOT / "_analysis"
OUT.mkdir(exist_ok=True)

summary: dict[str, object] = {"pdfs": {}, "docx": {}}

for path in sorted(ROOT.glob("*.pdf")):
    reader = PdfReader(path)
    pages: list[str] = []
    nonempty = 0
    for index, page in enumerate(reader.pages, start=1):
        text = page.extract_text(extraction_mode="layout") or ""
        text = text.encode("utf-8", errors="replace").decode("utf-8")
        if text.strip():
            nonempty += 1
        pages.append(f"\n\n===== PDF PAGE {index} =====\n\n{text}")
    output = OUT / f"{path.stem}.txt"
    output.write_text("".join(pages), encoding="utf-8")
    summary["pdfs"][path.name] = {
        "pages": len(reader.pages),
        "nonempty_pages": nonempty,
        "characters": sum(len(page) for page in pages),
        "output": str(output.relative_to(ROOT)),
    }

for path in sorted(ROOT.glob("*.docx")):
    doc = Document(path)
    lines: list[str] = []
    records: list[dict[str, object]] = []
    for index, paragraph in enumerate(doc.paragraphs):
        text = paragraph.text
        style = paragraph.style.name if paragraph.style else ""
        lines.append(f"P{index:04d}\t[{style}]\t{text}")
        records.append({"index": index, "style": style, "text": text})
    table_lines: list[str] = []
    for table_index, table in enumerate(doc.tables):
        table_lines.append(f"\n===== TABLE {table_index} ({len(table.rows)}x{len(table.columns)}) =====")
        for row_index, row in enumerate(table.rows):
            cells = [cell.text.replace("\n", " / ") for cell in row.cells]
            table_lines.append(f"R{row_index:03d}\t" + "\t".join(cells))
    (OUT / f"{path.stem}-paragraphs.txt").write_text("\n".join(lines), encoding="utf-8")
    (OUT / f"{path.stem}-tables.txt").write_text("\n".join(table_lines), encoding="utf-8")
    (OUT / f"{path.stem}-paragraphs.json").write_text(
        json.dumps(records, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    sections = []
    for section in doc.sections:
        sections.append({
            "page_width_mm": round(section.page_width.mm, 2),
            "page_height_mm": round(section.page_height.mm, 2),
            "top_margin_mm": round(section.top_margin.mm, 2),
            "bottom_margin_mm": round(section.bottom_margin.mm, 2),
            "left_margin_mm": round(section.left_margin.mm, 2),
            "right_margin_mm": round(section.right_margin.mm, 2),
            "header_distance_mm": round(section.header_distance.mm, 2),
            "footer_distance_mm": round(section.footer_distance.mm, 2),
        })
    styles = []
    for style in doc.styles:
        if style.type == 1:
            styles.append({
                "name": style.name,
                "font": style.font.name,
                "size_pt": style.font.size.pt if style.font.size else None,
                "bold": style.font.bold,
                "italic": style.font.italic,
            })
    summary["docx"][path.name] = {
        "paragraphs": len(doc.paragraphs),
        "tables": len(doc.tables),
        "sections": sections,
        "styles": styles,
    }

(OUT / "summary.json").write_text(
    json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(json.dumps(summary, ensure_ascii=False, indent=2))
