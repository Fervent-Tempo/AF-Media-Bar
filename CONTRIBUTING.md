# 为 AF Media Bar 做贡献

感谢你参与改进 AF Media Bar。开放的 Issue 是问题记录，不代表所有方案都已获准实施。

## 项目方向

以下方向不承诺发布日期或某个 Issue 一定会采用。具体需求与进度以 [Issues](https://github.com/Fervent-Tempo/AF-Media-Bar/issues) 和关联 PR 为准。

### 当前关注的方向

- **现有功能的稳定性：** 任务栏定位与避让、Explorer 恢复、多显示器和自动隐藏场景；媒体来源识别与控制；音频设备和退出时的资源清理。
- **歌词的可靠性：** 已支持来源的匹配、解析、时间轴和显示问题，以及可验证的本地缓存读取。新增来源或联网行为需要先讨论其数据来源、失败回退和维护成本。
- **可复现问题与易用性：** 有明确步骤、前后行为和 Windows 环境信息的修复；不改变既有设置语义的小范围文案、无障碍与体验改进。
- **更优秀的设计：** 更简洁、易用的设置页，更好的任务栏模式设计方案，更优秀的交互方式。


### 如何找到可认领的工作

- 优先查看 [`help wanted`](https://github.com/Fervent-Tempo/AF-Media-Bar/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22) 和 [`good first issue`](https://github.com/Fervent-Tempo/AF-Media-Bar/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)。普通开放 Issue 可能仍在调查或讨论。

### 需要先讨论的改动

新显示模式或大型 UI 重设计、设置格式不兼容变更、新的联网/遥测行为、播放器私有接口或缓存格式接入，以及跨多个服务的重构，都需要先在 Issue 中明确设计、兼容性、隐私和实机验证方案。请不要把技术储备或界面占位理解为已批准的开发计划。

## 开始之前

- 先搜索现有设置、文档、Issue 和 PR，确认功能尚不存在、工作没有重复。可复现缺陷和功能建议分别使用仓库的 Bug Report、Feature Request 表单。
- 对新功能、设置行为变更或任务栏、媒体会话、音频、歌词等高风险修改，请先在相关 Issue 下说明准备解决的问题、拟采用的方案和大致改动范围，等待维护者确认方向后再投入大量开发。简单的文案或明确的小修复可以直接提 PR。
- 开始较长的工作时，在 Issue 留言说明正在处理，并尽早建立关联的 Draft PR，让其他人看到进度。留言不是独占认领；如果方案或范围变化，请及时更新。维护者会协调重复工作。
- 安全问题不要公开提交 Issue，按 [SECURITY.md](SECURITY.md) 私下报告。

## 开发与验证

需要 Windows、`global.json` 指定的 .NET 10 SDK 和 PowerShell。解决方案位于 `src/AFMediaBar.slnx`。构建与测试请串行运行，避免 WPF 生成文件相互干扰：

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\src\AFMediaBar.slnx -r win-x64
dotnet build .\src\AFMediaBar.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -p:BuildInParallel=false
dotnet test .\tests\AFMediaBar.Layout.Tests\AFMediaBar.Layout.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

若修改 ViewModel，还需运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-architecture.ps1`。按改动范围做真实 Windows 验证；任务栏定位、Explorer 恢复、DPI、音频设备和播放器兼容性不能仅靠构建或单元测试证明。无法覆盖 Windows 10、Windows 11 或特定播放器时，请在 PR 中明确写“未验证”，不要求每位贡献者都拥有所有环境。普通 PR 不需要运行 `dotnet publish`。

## 改动边界

- 保持 WPF、ViewModel、服务和 Win32 的现有职责边界；释放新增的 COM 对象、原生 Hook、计时器和事件订阅。
- 未经事先讨论，不新增遥测、网络访问、设置格式不兼容改动或大范围 UI 重设计。
- 涉及界面文字时，请同时检查项目支持的语言。

## Pull Request

- 标题用简短的 `fix(scope): ...`、`feat(scope): ...` 或 `docs: ...` 说明结果；
- 填写 PR 模板：先用普通用户能理解的话说明问题和改动前后，再概述实现、验证证据、未验证场景及风险。构建通过不等于实机通过；界面与任务栏行为改动请尽可能附截图或短录屏。
- 用 `Refs #123` 关联进行中的 Issue；只有 PR 完整解决该问题时才用 `Closes #123`。保持 PR 范围与关联 Issue 一致。
- 可以使用 AI 辅助代码或撰写说明，但提交者必须亲自核对描述、测试结果和受影响范围。不要粘贴未经核实的 AI 分析或声称做过未执行的实机测试；优先写一个“改动前 → 改动后”的具体例子。
- 维护者可能要求拆分过大的改动，或在涉及系统行为时等待真实设备验收。

---

# Contributing to AF Media Bar

Thank you for helping improve AF Media Bar. An open issue records a problem, but does not mean every proposed solution has been accepted.

## Project direction

These directions are not a release schedule or a promise to implement any particular issue. See [Issues](https://github.com/Fervent-Tempo/AF-Media-Bar/issues) and linked PRs for individual work and its progress.

### Current areas of interest

- **Stability of existing features:** taskbar placement and avoidance, Explorer recovery, multi-monitor and auto-hide behavior, media-source discovery and controls, audio devices, and resource cleanup on exit.
- **Reliable lyrics:** matching, parsing, timing, and display for existing sources, plus verifiable local-cache reading. Discuss data sources, fallback behavior, and maintenance cost before adding a new source or network behavior.
- **Reproducible fixes and usability:** fixes with clear steps, before/after behavior, and Windows environment details; focused wording, accessibility, and experience improvements that preserve existing settings semantics.
- **Better design:** simpler, easier-to-use settings pages, better design approaches for the taskbar mode, and more effective interactions.

### Finding work to pick up

- Start with [`help wanted`](https://github.com/Fervent-Tempo/AF-Media-Bar/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22) or [`good first issue`](https://github.com/Fervent-Tempo/AF-Media-Bar/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22). A regular open issue may still be under investigation or discussion.

### Discuss before implementing

New display modes or major UI redesigns, incompatible settings changes, new network or telemetry behavior, private player APIs or cache formats, and cross-service refactors need prior discussion of design, compatibility, privacy, and real-device validation. Technical reserves and UI placeholders are not approved implementation plans.

## Before you start

- Search existing settings, documentation, issues, and PRs to check whether the feature already exists or someone is doing the same work. Use the Bug Report or Feature Request form as appropriate.
- For new features, settings behavior changes, or high-risk taskbar, media-session, audio, or lyrics work, comment on a related issue with the problem, proposed approach, and approximate scope. Wait for maintainer feedback before investing substantial work. Small documentation changes and clearly scoped fixes may go straight to a PR.
- For longer work, leave a progress comment on the issue and open a linked Draft PR early. A comment is not an exclusive reservation; update it if the approach changes. Maintainers will coordinate overlaps.
- Report security issues privately using [SECURITY.md](SECURITY.md), not a public issue.

## Development and validation

You need Windows, the .NET 10 SDK selected by `global.json`, and PowerShell. The solution is `src/AFMediaBar.slnx`. Run build and tests serially to avoid WPF-generated-file races:

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\src\AFMediaBar.slnx -r win-x64
dotnet build .\src\AFMediaBar.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -p:BuildInParallel=false
dotnet test .\tests\AFMediaBar.Layout.Tests\AFMediaBar.Layout.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

When changing a ViewModel, also run `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-architecture.ps1`. Test relevant behavior on a real Windows desktop: build and unit tests alone cannot establish taskbar placement, Explorer recovery, DPI, audio-device, or player compatibility. If you cannot test a Windows version or player, say “not tested” in the PR; contributors are not expected to own every environment. Ordinary PRs do not need `dotnet publish`.

## Scope and architecture

- Preserve the current responsibilities of WPF views, ViewModels, services, and Win32 code. Release any new COM objects, native hooks, timers, and event subscriptions.
- Discuss telemetry, new network access, incompatible settings changes, and large UI redesigns before implementing them.
- Check all supported languages when changing UI text.

## Pull requests

- Use a short result-oriented title such as `fix(scope): ...`, `feat(scope): ...`, or `docs: ...`.
- Complete the PR template. Explain the user problem and before/after behavior in plain language first, then summarize implementation, actual verification, untested scenarios, and risks. A successful build is not real-device verification; include a screenshot or short recording when practical for UI or taskbar behavior.
- Use `Refs #123` for related work. Use `Closes #123` only when the PR fully resolves the issue. Keep the PR aligned with its linked issue.
- AI may assist with code or writing, but the submitter must verify every claim, test result, and affected area. Do not paste unverified AI analysis or claim device testing that was not done. A concrete “before → after” example is more useful.
- Maintainers may ask to split broad changes or wait for real-device validation of system behavior.
