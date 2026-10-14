# 翻译贡献指南

English instructions are available under [Translation guide](#translation-guide).

## 语言与文件

欢迎所有语言的翻译贡献，包括新增界面语言、翻译 README 和修正已有译文。界面目前支持四种语言，译文在仓库中维护；下表列出已接入的资源与 README。

| 语言 | 界面资源 | README |
| --- | --- | --- |
| 简体中文 | [StringsZhHans.resx](../src/AFMediaBar/Resources/StringsZhHans.resx) | [中文](../README.md) |
| 繁體中文 | [StringsZhHant.resx](../src/AFMediaBar/Resources/StringsZhHant.resx) | — |
| English | [StringsEn.resx](../src/AFMediaBar/Resources/StringsEn.resx) | [English](../README.en-US.md) |
| Tiếng Việt | [StringsVi.resx](../src/AFMediaBar/Resources/StringsVi.resx) | [Tiếng Việt](../README.vi-VN.md) |

## 报告翻译问题

使用 [翻译反馈表单](https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=localization.yml)，注明语言、页面或资源键、当前文案与建议译文。文字截断或布局问题请附截图，分享前删除隐私信息。希望贡献新语言时，欢迎说明语言名称并提供已有译文或希望参与的内容。新增界面语言所需的语言选择与资源加载接入由维护者协同完成。

## 修改译文

1. 先阅读 [贡献指南](../CONTRIBUTING.md)；使用 AI 辅助时同时遵守 [AGENTS.md](../AGENTS.md)。
2. 在对应 `.resx` 文件中修改 `<value>`，保留 `<data name="…">` 的资源键。修正已有译文无需更改 C# 代码。
3. 保留 `{0}` 等格式占位符、换行与有意义的标点。保持 XML 有效，使用 `&amp;`、`&lt;` 表示文案中的 `&`、`<`。
4. README 修改同步中、英、越三版的章节顺序、功能与限制、链接及构建命令。语言之间可自然表达，不加入单个版本独有的算法或内部流程。
5. 按贡献指南串行构建与测试；在应用中切换受影响语言，检查设置页、弹窗、工具提示与长文案。无法实机检查时在 PR 中写明“待维护者验收”。

PR 写清修改的语言、位置和原因，附实际检查结果；显示问题可附修改前后的截图。不要把有对应资源键等同于翻译准确或布局已验收。

---

# Translation guide

## Languages and files

Translation contributions in every language are welcome, including new interface languages, README translations, and corrections to existing wording. The interface currently supports four languages. The table below lists the resources and README editions already integrated into the project.

| Language | Interface resource | README |
| --- | --- | --- |
| 简体中文 | [StringsZhHans.resx](../src/AFMediaBar/Resources/StringsZhHans.resx) | [Chinese](../README.md) |
| 繁體中文 | [StringsZhHant.resx](../src/AFMediaBar/Resources/StringsZhHant.resx) | — |
| English | [StringsEn.resx](../src/AFMediaBar/Resources/StringsEn.resx) | [English](../README.en-US.md) |
| Tiếng Việt | [StringsVi.resx](../src/AFMediaBar/Resources/StringsVi.resx) | [Vietnamese](../README.vi-VN.md) |

## Reporting translation issues

Use [translation feedback](https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=localization.yml). Include the language, page or resource key, current wording, and a suggested translation. Attach screenshots for clipped text or layout problems, removing private information first. To contribute a new language, share its name and any translations you have, or describe how you would like to help. Maintainers will help coordinate language selection and resource loading for new interface languages.

## Editing translations

1. Read the [contribution guide](../CONTRIBUTING.md) and, when using AI assistance, [AGENTS.md](../AGENTS.md).
2. Edit `<value>` in the relevant `.resx` file, preserving the resource key in `<data name="…">`. Correcting existing wording does not require C# changes.
3. Preserve format placeholders such as `{0}`, line breaks, and meaningful punctuation. Keep the XML valid; use `&amp;` and `&lt;` for literal `&` and `<` in text.
4. Keep the Chinese, English, and Vietnamese READMEs aligned in section order, features, limitations, links, and build commands. Use natural wording without adding algorithms or internal workflows to one edition alone.
5. Build and test serially as described in the contribution guide. Switch to each affected language in the app and inspect settings, popovers, tooltips, and long text. If device checks are unavailable, mark them as awaiting maintainer verification in the PR.

Explain the language, location, and reason for the change, and report checks actually performed. Before/after screenshots help with display issues. Having a resource key does not establish translation quality or a verified layout.
