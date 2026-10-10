# 设置契约：按需读取

基线：main 功能提交 `09e9cf1`；正式版 `v1.3.2`；schema `2`；核对日 2026-10-10。清单是已核对快照，源码变化时先重新核对。

## 默认读取顺序

1. 读本入口，然后查询本次涉及的字段。不要默认全文读取 JSON 或完整参考。
2. 查询返回字段、关联枚举、兼容规则及 relatedPaths；联动修改再查关联路径。
3. 需要更多背景才读对应专题；发布、历史清点或校验失败调查才读完整存档。

```powershell
pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Query -Path settings.taskbarExperience.artworkHoverMode
pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Generate
pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Verify
```

Query也支持 `-Prefix`、`-Enum`、`-List`专题目录。前缀默认最多8项，超出直接拒绝；可显式提高 `-MaxFields`，最多32。`-Details` 才增加原始/发布默认、完整读取结果和范围。无此路径不等于未发布，先查 tag；历史删除项可按原路径查询。

## 判断规则

- 正式 release/tag 决定是否已发布；main、PR和开发版本号不是发布证据。
- 未发布项可直接调整，无需迁移开发旧值；已发布变更明确旧数据处理选择、影响与验证，允许有意不兼容。
- 枚举成员独立标记发布状态；父字段已发布不代表新增成员也已发布。
- 内置默认、缺字段、显式 null 不同，尤其嵌套结构；重置优先用户快照，不能从查询的内置默认推定。
- 页面位置不决定存储归属，旧XAML存在不等于入口可达；实际导航和重置调用需核对源码。

## 专题与完整存档

| 修改范围 | 按需专题 |
|---|---|
| 字体、主题、颜色、表面 | [外观](settings-contracts/appearance.md) |
| 静置内容、封面、三层 | [任务栏](settings-contracts/taskbar.md) |
| 歌词处理、布局、来源 | [歌词](settings-contracts/lyrics.md) |
| 点击、滚轮、托盘 | [交互](settings-contracts/interaction.md) |
| 来源过滤、通知、快速启动 | [媒体](settings-contracts/media.md) |
| 频谱、性能参数 | [组件](settings-contracts/components.md) |
| 语言、更新、启动状态 | [应用](settings-contracts/application.md) |
| 显示器、定位、旧模式字段 | [定位](settings-contracts/placement.md) |

[完整参考](settings-contracts/reference.md)保留存储、兼容、历史及全部字段证据；[机器存档](settings-contracts.json)供工具使用。完整存档不是普通修改的默认上下文。

维护字段数据改机器清单，关联摘要改查询索引；运行 `tools/settings-contracts.ps1 -Action Generate` 生成专题，再运行 `tools/settings-contracts.ps1 -Action Verify`。发布时核对实际tag，更新标记与正式默认快照；源码指纹只在重新核实后更新。
