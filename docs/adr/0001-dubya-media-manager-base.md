# 0001: 继续使用 Dubya.WindowsMediaController，命令穿透 ControlSession

MVVM 版恢复旧版 MediaSessionService 的控制能力时，决定继续使用 Dubya.WindowsMediaController 2.5.6 的 MediaManager 作为会话底座，而不是回退到旧版（根目录 AF-Media-Bar）使用的裸 `GlobalSystemMediaTransportControlsSessionManager`。

原因：Dubya 已封装会话枚举、焦点会话与事件分发（OnAnySessionOpened/Closed、OnFocusedSessionChanged、OnAnyTimelinePropertyChanged），而这些正是旧版手写维护的部分；该包不提供类型化的命令方法，播放/暂停/上一首/下一首通过公共属性 `MediaSession.ControlSession`（原始 GSMTC 会话）直接调用 `TryTogglePlayPauseAsync` 等。重连语义映射为 `MediaManager.ForceUpdate()`。回退裸 GSMTC 意味着重写 Dubya 已经解决的基础设施，没有新收益。

**Status**: accepted
