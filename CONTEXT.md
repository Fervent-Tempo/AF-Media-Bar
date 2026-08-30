# AF-Media-Bar (MVVM)

任务栏媒体状态应用（MVVM 重构版）：监听系统的 SMTC 会话，构建统一媒体快照并渲染在任务栏窗口。本应用不内嵌播放器，只观察与控制外部应用注册的媒体会话。

## Language

**SMTC 会话（SMTC Session）**:
外部应用（如网易云音乐）向 Windows 注册的媒体会话。本应用是观察者与控制器，从不拥有播放器。
_Avoid_: 播放器、媒体源（媒体源指会话所属的应用）

**活跃会话（Active Session）**:
当前被选中的 SMTC 会话——快照反映它的状态，控制命令作用在它身上。选择策略是自动（焦点会话优先）加手动覆盖。
_Avoid_: 当前会话（与系统焦点会话混淆）

**MediaSnapshot**:
某一时刻媒体状态的统一快照：标题、艺术家、封面、播放状态、位置、歌词。
_Avoid_: 歌曲信息（songInfo 是原始元数据，Snapshot 是加工后的完整状态）

**快照刷新（Snapshot refresh）**:
重新构建当前 MediaSnapshot 的过程，由 SMTC 事件或手动请求触发，结果通过 SnapshotChanged 发布。
_Avoid_: SMTC 更新（"更新"指读取侧；向会话发命令是另一件事）

**控制命令（Control command）**:
向活跃会话发送的操作：播放/暂停、上一首、下一首、进度跳转、切换会话。
_Avoid_: 播放控制（语义相同，但"控制命令"与"快照刷新"的读写二分更清晰）

**歌词（Lyrics）**:
与当前播放位置同步的歌词文本（LRC 及可选译文），由 MediaSnapshot 携带，任务栏 UI 渲染。网易云歌词按歌曲 id 精确获取。
_Avoid_: 字幕、滚动文本（歌词是数据，渲染形态是另一回事）

**来源回退（Source fallback）**:
构建快照的数据来源策略：内存轮询（网易云）优先，轮询失败时回退到 SMTC 会话读取；非网易云会话直接走 SMTC。
_Avoid_: 内存快照、SMTC 快照（快照是产物，来源是过程）

**MediaSessionService**:
管理 SMTC 会话的服务：选择活跃会话、构建并发布 MediaSnapshot、执行控制命令。UI 只通过它的事件和命令接口交互，不直接接触系统 SMTC API。
_Avoid_: SessionManager、SmtcHandler（旧版与新版都叫这个名字，保持一致）
