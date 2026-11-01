# 维护快速开始轮播

轮播已经通过 `HomeContentService` 读取远程 JSON。主地址为本仓库 `release-metadata` 分支的 `home/catalogue.json`，备用地址是 jsDelivr 上的同一文件：

- `https://raw.githubusercontent.com/Fervent-Tempo/AF-Media-Bar/release-metadata/home/catalogue.json`
- `https://cdn.jsdelivr.net/gh/Fervent-Tempo/AF-Media-Bar@release-metadata/home/catalogue.json`

有有效目录时，轮播只展示目录中的条目，顺序与文件一致，最多显示前五条。没有可用目录时，才使用版本亮点和内置介绍。联网失败或目录格式错误时保留上一次有效缓存，首次离线使用内置内容。

## 发布与更新

1. 复制 [目录示例](examples/home-catalogue.json) 到 `release-metadata` 分支的 `home/catalogue.json`。示例包含四种界面语言和现有截图地址，可按需删改条目。
2. 在该分支提交并推送目录。之后修改同一文件即可调整显示内容，无需重新发布程序。
3. 在快速开始点击「刷新动态」。自动读取的缓存有效期为六小时；手动刷新跳过该缓存期限，但请求和按钮仍有五分钟冷却。刚更新时 CDN 也可能仍返回缓存。

本文和示例不自动发布远程目录。仅提交到主分支的示例不会使在线目录生效。

## 条目字段

| 字段 | 规则 |
| --- | --- |
| `schemaVersion` | 固定为 `1` |
| `items` | 1–30 个条目；前五条用于轮播 |
| `id` | 目录内唯一、稳定的标识，最多 64 个字符；修改文案时保留该标识 |
| `kind` | `release`、`feature` 或 `development`，决定标签 |
| `version` | 可选版本号，不含 `v` 前缀 |
| `image` | 可选 HTTPS 图片地址，只接受本仓库的 raw.githubusercontent.com 或 jsDelivr 地址；缺省显示内置示意预览 |
| `url` | 可选详情地址，只接受本 GitHub 仓库及其子路径；不提供时进入仓库首页 |
| `localizations` | `zh-Hans`、`zh-Hant`、`en`、`vi`；每种语言包含 `title` 和 `summary` |

未正式发布的内容使用 `development` 标签。标题最多 180 个字符，摘要最多 1000 个字符。缺少当前语言时优先使用简体中文，再使用目录中的第一种语言，并显示原文标签。建议同时维护四种语言，摘要保持简短。

图片使用 WPF 支持的静态格式（如 PNG/JPEG），最大 2 MiB、边长 4096 像素；客户端最多缓存十张，展示解码宽度为 1600 像素。

图片可放在同一分支的 `home/images/` 中，引用对应 raw 地址。替换图片时使用新文件名或带版本的地址，避免旧图片缓存继续命中。目录不会执行命令、安装插件或读取用户使用数据。

## 播放器支持说明

可选`support`列表的条目为`key`和`localizations`。语言对象的`title`是接入说明，`summary`是插件条件；两项均是纯文本。key必须对应程序已知类别，例如`Spotify`、`mpv`或`Service.MediaSource.NetEaseCloudMusic`。未知类别不会新增播放器适配，也不能改变来源ID。

当前会话的控制能力从已有快照读取；未选中的会话不额外查询。没有歌词或未检测插件不等于不支持或未安装。支持说明应写明核实过的版本和条件，不把通道接入方式当成所有版本兼容保证。

## 项目动态

统计缓存1小时，精选目录6小时；手动强制刷新至少间隔5分钟。GitHub请求串行，采用ETag，遇限流暂停，不内置Token。未知计数为“—”，不是0；失败保留旧快照并显示缓存时间。

Issue搜索明确排除PR；近7天按创建时间统计所有状态。PR只称“开放”，草稿单独标记，不声称可以合并。条目只显示编号、原始标题和日期，不执行其正文。

页面离开取消等待，服务由应用容器拥有。轮播只在首页主卡可见、窗口激活且允许连续动效时运行；鼠标悬停或键盘聚焦时暂停，离开后重新计时。手动切换不会永久暂停。
