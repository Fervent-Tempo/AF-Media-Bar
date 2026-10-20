# 设置发布登记

[登记文件](settings-contracts.json)按用户配置的结构记录首次正式发布版本；`unreleased` 表示开发中。它是开发资料，不是用户配置，也不参与程序读取设置。

## 按需查询

```powershell
pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Query -Path settings.appearance.idleNoteColor
pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Query -Enum InterfaceLanguage
pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Query -List
```

查询返回发布状态、相关枚举、兼容例外及关联路径。`-Prefix` 默认最多 8 项，可用 `-MaxFields` 提高至 32；未知路径不能视为未发布，先查源码及正式 tag。历史删除项也按原路径查询。

## 记录方式

- `settings` 保持实际 JSON 键名和嵌套关系；叶子值为 `vX.Y.Z` 或 `unreleased`。
- 对象用 `$release` 记录自身发布时间，子字段各自标记。对象列表用 `$items` 记录条目模板，查询路径采用 `entries[].target`。
- `enums` 记录关联字段及各成员的数值、发布时间。父字段已经发布，不代表新增成员也已发布；已发布数值不能随意重排。
- `removed` 保留发布后删除的字段、版本与当前处理；删除尚未正式发布时，`firstAbsentRelease` 标记 `unreleased`。`rules` 只记录必要兼容例外，不复刻所有字段的默认值和归一化细节。

例如，当前 `settings.appearance.fontWeight` 为 `v1.2.0`，`settings.appearance.idleNoteColor` 为 `v1.4.0`。这些值表示发布时间，不表示字体字重或颜色。

## 修改与发布

1. 修改字段或枚举时查询其状态，核对目标工作区的实际序列化、默认值、缺字段/null、Normalize、克隆和重置调用；按改动运行相关测试。
2. 未发布项无需迁移开发中间构建的数据；已发布变更明确旧数据处理、用户影响和验证，允许有意不兼容。
3. 新字段/成员标记开发中。准备正式发布时核对 release/tag 中的实际内容，再更新首次发布标记及最新正式版；进入 main 或改开发版本号不算发布。
4. 设置 schema 仍只在发布批次处理。默认值、范围和读取行为查源码及相关测试；重置优先用户默认快照，页面位置不决定存储或重置归属。

查询会检查 JSON、发布标记及引用格式。需要单独检查登记时运行：

```powershell
pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Verify
```

这只是可选格式检查，不扫描源码、不核验本地 tag，也不证明字段覆盖、默认语义、兼容安全或桌面体验。普通业务改动不要求更新源码指纹；不再生成专题和完整快照。
