# 0002: 恢复 NetEase 内存轮询作为网易云歌词/进度的来源

网易云会话的快照（标题、进度、歌词、song id）恢复使用 233ms 内存轮询（扫描 cloudmusic.dll 模式 + 解析 playingList 文件），而不是依赖 SMTC。

原因：GSMTC 不推送连续进度（`TimelineProperties` 只在跳转/切歌时变化），也不暴露网易云 song id——而歌词精确取词（NetEaseLyricsProvider 按 `NetEaseSongId`）和实时行同步都依赖这两个数据。来源策略是回退制：内存轮询优先，失败（进程退出、cloudmusic.dll 模式变化）时回退到 SMTC 读取，保证主链路不白屏。

代价已确认：内存指针扫描在网易云客户端更新后可能失效（旧版即有此问题），回退策略正是为此设计；非网易云会话不轮询，歌词走 Lrclib 按标题/艺术家兜底。

**Status**: accepted
