<!-- owner: AF Media Bar maintainers | verified_at: 0aba7f96 | review_by: 2027-03-05 -->

# 设计约束

本目录记录跨模块的意图、所有权、失败模式和真机验收边界。它不记录当前实现状态、完整类清单或逐步调用链；这些以源码、测试和 CodeGraph 为准。修改对应行为时先读相关篇目，并在同一 PR 更新 `verified_at`。

| 主题 | 何时读 | 文档 |
|---|---|---|
| 任务栏与悬浮宿主 | 停靠、几何、自动隐藏、避让、组件布局、退出 | [taskbar.md](taskbar.md) |
| 主题与外观 | 主题、强调色、材质、取色、图标、跑马灯 | [appearance.md](appearance.md) |
| 媒体与歌词 | 来源身份、双通道、选择、歌词、缓存、封面 | [media-lyrics.md](media-lyrics.md) |
| 外部播放器联动 | Nons 队列/随机、companion 能力、版本冲突与待补接口 | [nons-companion.md](nons-companion.md) |
| 设置 | 字段、兼容、重置、持久化、页面组织 | [settings.md](settings.md) |
| 输入与通知 | Raw Input、托盘、曲目通知 | [input-notifications.md](input-notifications.md) |
| 平台运行 | 音频、频谱、指标、日志、更新、名单、剪枝 | [platform-runtime.md](platform-runtime.md) |

写作规则：删掉它会改变审查或实现决策，且代码、测试、Git 回答不了，才写。路径、类名、调用图、当前进度、测试计数和一次性任务不写。
