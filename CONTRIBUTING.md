> The English documentation is provided in the second half of this document.
# 为 AF Media Bar 做贡献


感谢你帮助改进 AF Media Bar。

## 开始之前

- 提交前请先搜索现有 Issue，避免重复提交。
- 可复现缺陷请使用 bug report 表单，功能建议请使用 feature request 表单。
- 请保持改动聚焦，避免在同一次改动中混入无关格式化或重构。
- 安全漏洞必须通过 [SECURITY.md](SECURITY.md) 中的方式报告，不要发布为公开 Issue。

## 开发环境

- Windows 10 version 1809 or later, or Windows 11
- .NET 10 SDK（使用 `global.json` 选定的版本）
- PowerShell

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\AFMediaBar.slnx
dotnet build .\AFMediaBar.slnx -c Debug --no-restore
```

## 验证

提交改动前请运行：

```powershell
dotnet build .\AFMediaBar.slnx -c Release --no-restore
dotnet test .\AFMediaBar.slnx -c Release --no-build
dotnet publish .\src\AFMediaBar\AFMediaBar.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\AFMediaBar-win-x64
git diff --check
```

如果改动影响行为，请在 Windows 10 和 Windows 11 上验证相关任务栏流程。音频相关改动应尽量使用至少一个桌面播放器和一个浏览器音源进行检查。

## 改动准则

- 保持现有 WPF 与 Win32 的职责边界。
- 请确定性释放 COM 对象、原生 hook、非托管缓冲区、计时器和事件订阅。
- 未经过明确设计讨论并补充隐私文档前，不要添加遥测或网络访问。
- 面向用户可见的变更请记录到 `CHANGELOG.md`。
- 适用时，请同时维护 `README.md` 和 `README.en-US.md` 中的公开文档。

## Pull Request

请描述问题、选择的行为、已完成的验证，以及任何特定 Windows 版本上的风险。视觉改动建议附带截图。维护者可能会要求将范围较大的改动拆分为更小的提交。

---

# Contributing to AF Media Bar


Thank you for helping improve AF Media Bar.

## Before You Start

- Search existing Issues before opening a duplicate.
- Use the bug report form for reproducible defects and the feature request form for proposals.
- Keep changes focused. Avoid unrelated formatting or refactoring in the same change.
- Security vulnerabilities must be reported through [SECURITY.md](SECURITY.md), not a public Issue.

## Development Environment

- Windows 10 version 1809 or later, or Windows 11
- .NET 10 SDK (use the version selected by `global.json`)
- PowerShell

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\AFMediaBar.slnx
dotnet build .\AFMediaBar.slnx -c Debug --no-restore
```

## Validation

Before submitting a change:

```powershell
dotnet build .\AFMediaBar.slnx -c Release --no-restore
dotnet test .\AFMediaBar.slnx -c Release --no-build
dotnet publish .\src\AFMediaBar\AFMediaBar.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\AFMediaBar-win-x64
git diff --check
```

For behavior changes, verify the relevant taskbar workflow on Windows 10 and Windows 11. Audio changes should be checked with at least one desktop player and one browser source when possible.

## Change Guidelines

- Preserve the existing WPF and Win32 ownership boundaries.
- Release COM objects, native hooks, unmanaged buffers, timers, and event subscriptions deterministically.
- Do not add telemetry or network access without an explicit design discussion and privacy documentation.
- Document user-visible changes in `CHANGELOG.md`.
- Keep public documentation in both `README.md` and `README.en-US.md` when applicable.

## Pull Requests

Describe the problem, the chosen behavior, validation performed, and any Windows-version-specific risk. Screenshots are useful for visual changes. Maintainers may request that broad changes be split into smaller submissions.
