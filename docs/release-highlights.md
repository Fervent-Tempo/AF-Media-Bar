# 版本亮点目录

设置页的「更新亮点」只负责阅读；检查、下载、跳过和安装仍归应用页及 UpdateService。目录与安装清单分开，不影响 pending.json 或安装状态。

## 维护

1. 内置离线历史位于 src/AFMediaBar/Resources/ReleaseHighlights.json，只收录已正式发布的版本，保留 zh-Hans、zh-Hant、en 摘要；可补充 vi。不要复制完整 CHANGELOG，也不将预发布版混入正式历史。
2. 运行 pwsh -NoProfile -File tools/prepare-release-highlights.ps1 -VerifyOnly，检查内置三语言、日期、版本唯一性和官方链接。去掉 -VerifyOnly 可生成本地产物；不要用它覆盖线上中文历史。
3. 线上 release-metadata/release/highlights.json 由 Convert Preview to Stable 工作流维护：先用 tools/update-release-highlights.ps1 从已审核的 latest-preview.json 追加中文文案，再验证发布包、公开 Release，并提交稳定清单和亮点目录。摘要取自候选清单的 title、changelog 与可选 releaseHighlights；已有文案与分组在重跑时保留，审核元数据时一并核对。
4. 维护者验收后显式运行稳定晋级，工作流刷新亮点 CDN 缓存并触发元数据同步。内置历史在后续发布准备时补齐已正式发布的版本；本地生成脚本不发布远端资料。

## 契约

顶层 schemaVersion 为 1，releases 为非空数组。每个条目包含三段或四段数字 version、yyyy-MM-dd 格式 releaseDate、官方 GitHub Release 的 releaseNotesUrl，以及 localizations。每个已提供的语言包含非空 title 与 highlights 字符串数组，可附 content；线上目录目前只维护 zh-Hans。主题按用户可感知的变化组织，每个更新项使用一句话标题和具体说明。

客户端按数字版本倒序排列，合并等价版本（例如 1.3.1 与 1.3.1.0），重复目录版本会使刷新失败。当前更新清单中的版本缺少目录条目时补为原文；已有人工译文优先。缺失语言明确标记原文，不实时机器翻译。内容以 WPF 纯文本显示；外链只允许项目官方 HTTPS Release 页面。

## 文案格式与兼容

从 1.5.0 起，后续版本亮点统一采用以下格式，当前版本中文文案以 `RELEASE_NOTES.md` 为来源。版本简介始终可见，用一句简短的话概括主要变化；实验功能的开启入口、默认关闭和测试限制写入对应条目详情。用少量简洁的二级标题划分主题，每个主题用一个 emoji 辅助识别，每个更新项使用一个默认收起的折叠块，不逐项重复添加表情。标题说明变化结果，详情保留前置条件、必要操作、默认行为和限制，不写内部实现。软件内版本号和日期作为主要标题信息，版本主题作为次级说明；各条目使用独立卡片，右侧“展开／收起”提示和箭头复用其它设置页模板并垂直居中；标题区整行可展开，提供悬停与键盘焦点反馈，正文字号与设置行一致。

每个版本都应有简短的主题描述，例如“设置与任务栏体验更新”，不能只重复软件名和版本号。发布说明一级标题使用 `# AF Media Bar 版本号 — 版本主题`，生成的目录 `title` 保留完整标题；软件已单独显示版本号和日期，展示主题时去掉重复前缀。历史版本没有主题时，核对该版本发布内容后补充，不自动猜测或重写原有亮点列表。

```markdown
# AF Media Bar 1.5.0 — 桌面悬浮、竖向任务栏与音频控制

新增多种显示方式，优化歌词和日常交互。

## ✨ 更多显示方式

<details>
<summary>新增桌面悬浮模式</summary>

在“实验性功能”页开启桌面悬浮实验后，在“显示与位置”切换。可在桌面上拖动媒体栏，选择横向或竖向布局，并按显示器记住位置。

此实验默认关闭，尚未经充分测试，遇到问题欢迎反馈。其他必要说明可另起一段。

</details>

## 下载与安装

下载、安装和已知限制继续使用普通正文，不放进功能折叠块。
```

折叠标签各占一行，不添加 `open` 等属性，不嵌套；主题、条目标题和详情使用纯文本，详情只使用段落。GitHub 原生呈现折叠块，应用以 WPF 控件显示，不执行 HTML。旧历史的普通段落与列表仍受支持，不自动拆分为新结构。提取在下载、已知限制或贡献者章节前停止。

`tools/release-notes-content.ps1` 是清单和目录脚本共用的提取入口。`tools/new-release-manifest.ps1` 自动生成旧版 `changelog` 与新增的 `releaseHighlights`；目录把后者保存为对应语言的 `content`。两处结构相同：`summary` 为简介字符串，`sections` 为主题数组；主题含 `title` 和 `items`，条目含非空 `title`、`description`。简介及详情中的空行保留为段落。

schema 保持 1。必须保留非空的 `changelog` / `highlights` 字符串数组；兼容条目由主题、标题和完整详情生成，简介单独占一条，不包含 HTML 标签。为避免旧清单读取器只显示前 20 条，生成阶段超过 20 条即报错，不截断正文。旧程序忽略新字段并显示平铺内容；新程序优先显示有效分组，扩展内容缺失或无效时整段回退到旧数组，不影响更新与安装。发布脚本对无效扩展报错，不覆盖有效文件。

## 已发布版本的文案回填

普通晋级不覆盖已有版本。需要更新已经审核的指定版本文案时，先复制现有目录到本地候选文件，再显式执行：

```powershell
pwsh -NoProfile -File tools/update-release-highlights.ps1 -ManifestPath artifacts/release-highlights-1.5.0/latest.json -CataloguePath artifacts/release-highlights-1.5.0/highlights.json -ReleaseNotesPath RELEASE_NOTES.md -ReplaceVersionText
```

文档与清单的版本必须一致；回填只能替换该版本的中文文案，不能更改发布日期或 Release 链接。清单应使用对应版本现有的已审核元数据，不重新构造安装包地址、大小或哈希。更早历史保留；重复执行得到相同结果。生成候选文件不构成修改线上目录或已发布 Release 的授权。

## 读取与测试

服务在页面打开或手动刷新时读取，启动不额外联网。先用内置快照或 %LOCALAPPDATA%/AFMediaBar/cache/release-highlights.json，再尝试 raw 与 jsDelivr。有效缓存 24 小时内复用，手动刷新绕过期限。响应限 2 MiB，每个来源 15 秒超时；无效数据、断网或失败不覆盖有效缓存。页面离开取消请求，代际检查阻止过期结果发布。

AFMEDIABAR_RELEASE_HIGHLIGHTS_URL 可替换全部来源为本地 JSON 文件、HTTPS 或回环 HTTP 测试端点。它只影响亮点目录。线上目录不可用时，客户端继续使用有效缓存或内置正式版本快照，并提示刷新失败。

调试构建同样会读取上述缓存和线上目录，仅重新编译页面不会把旧字符串列表转换为分组卡片。预览本地候选时，应在启动调试进程前将 `AFMEDIABAR_RELEASE_HIGHLIGHTS_URL` 指向生成的 `highlights.json` 完整路径，再重新启动调试并打开更新亮点页。进程启动后才设置变量不会影响已经运行的程序；文件没有 `content` 字段时仍按旧列表显示。
