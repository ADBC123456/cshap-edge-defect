# electrical-thesis-writing

面向电气工程与智能控制专业应用型本科毕业设计的写作与审校 Skill。

## 入口与组成

- `SKILL.md`：触发描述、优先级和强制工作流。
- `references/style-profile.md`：两篇往年论文的风格画像、量化节奏和负面样本边界。
- `references/chapter-recipes.md`：摘要、绪论、方案、硬件、软件、测试和结论的分章写法。
- `references/sentence-bank.md`：可替换的工程句式骨架。
- `references/compliance-checklist.md`：2027届模板与2026版专业规范。
- `references/worked-examples.md`：不推荐与推荐写法的对照校准。
- `assets/project-brief.md`：写作前的事实收集表。
- `scripts/thesis_style_lint.py`：TXT、Markdown和DOCX风险检查。

## 调用示例

- “使用 electrical-thesis-writing，根据我提供的项目材料拟出三级提纲，并列出每节需要的图和证据。”
- “使用 electrical-thesis-writing 改写这一段，保留所有数据和引用，不增加未经验证的效果。”
- “使用 electrical-thesis-writing 写系统测试章。没有原始数据的地方标为待补，不要编造。”
- “使用 electrical-thesis-writing 按2027届模板审查全文，并检查口语、AI套话、图表引用和结论边界。”

## 审校脚本

```bash
python scripts/thesis_style_lint.py <论文.txt或.docx> --json <报告.json>
```

TXT和Markdown只依赖Python标准库；DOCX读取需要`python-docx`。脚本报告仅用于人工复核，不评估AI率，不验证事实真伪。
