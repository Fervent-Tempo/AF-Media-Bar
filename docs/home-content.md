# 快速开始内容维护

首页首先使用内置内容和本地缓存，再异步刷新公开资料。GitHub统计只读，不依赖使用统计；main中不包含统计上传。

## 精选图文

线上目录地址为仓库`release-metadata`分支的`home/catalogue.json`，另有现有jsDelivr来源回退。目录尚未发布或网络失败时继续显示内置资料，不影响其它设置。维护文件并发布目录属于单独操作，本轮没有推送线上资料。

```json
{
  "schemaVersion": 1,
  "items": [
    {
      "id": "feature-example",
      "kind": "development",
      "version": null,
      "image": null,
      "url": "https://github.com/Fervent-Tempo/AF-Media-Bar",
      "localizations": {
        "zh-Hans": { "title": "功能名称", "summary": "简要说明作用与适用条件。" },
        "en": { "title": "Feature name", "summary": "A short description and its conditions." }
      }
    }
  ],
  "support": []
}
```

- 类型仅为`release`、`feature`、`development`；未正式发布的内容必须使用`development`。正式版本摘要以现有版本目录为主，不按程序中写死的版本号生成。
- 每个条目使用固定且唯一的id。目录最多30条，首页最多显示5张；标题和说明保持简洁。建议提供简中、繁中、英文、越南文，缺译显示原文标识。
- 链接只允许本项目GitHub HTTPS地址；图片只允许本仓库的raw或jsDelivr地址。图片采用PNG/JPEG等WPF支持的静态格式，最大2 MiB、边长4096像素；本地最多缓存10张，使用1600像素展示解码。
- 应用不执行目录中的HTML、脚本或本地命令。目录不能改变安装状态、统计同意或上传地址。
- 两张内置开发截图来自原生设置验收，不代表已经正式发布；正式发布前应更新图文与标签。

## 播放器支持说明

可选`support`列表的条目为`key`和`localizations`。语言对象的`title`是接入说明，`summary`是插件条件；两项均是纯文本。key必须对应程序已知类别，例如`Spotify`、`mpv`或`Service.MediaSource.NetEaseCloudMusic`。未知类别不会新增播放器适配，也不能改变来源ID。

当前会话的控制能力从已有快照读取；未选中的会话不额外查询。没有歌词或未检测插件不等于不支持或未安装。支持说明应写明核实过的版本和条件，不把通道接入方式当成所有版本兼容保证。

## 项目动态

统计缓存1小时，精选目录6小时；手动强制刷新至少间隔5分钟。GitHub请求串行，采用ETag，遇限流暂停，不内置Token。未知计数为“—”，不是0；失败保留旧快照并显示缓存时间。

Issue搜索明确排除PR；近7天按创建时间统计所有状态。PR只称“开放”，草稿单独标记，不声称可以合并。条目只显示编号、原始标题和日期，不执行其正文。

页面离开取消等待，服务由应用容器拥有。轮播只在首页主卡可见、窗口激活且允许连续动效时运行；键盘聚焦或手动切换后需主动恢复。
