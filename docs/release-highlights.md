# 版本亮点目录

设置页的「更新亮点」只负责阅读；检查、下载、跳过和安装仍归应用页及 UpdateService。目录与安装清单分开，不影响 pending.json 或安装状态。

## 维护

1. 在 src/AFMediaBar/Resources/ReleaseHighlights.json 中追加经过核对的正式版本摘要，覆盖 zh-Hans、zh-Hant、en。不要复制完整 CHANGELOG，也不将预发布版混入正式历史。
2. 运行 pwsh -NoProfile -File tools/prepare-release-highlights.ps1 -VerifyOnly，确认三语言、日期、版本唯一性和官方链接。去掉 -VerifyOnly 可生成 artifacts/metadata/highlights.json。
3. 经维护者审核后，另行将产物发布到 release-metadata 分支的 release/highlights.json。本脚本只生成本地文件；本次功能开发没有发布远端目录，也没有修改发布工作流。更新安装清单时同步补齐亮点，减少原文回退。

## 契约

顶层 schemaVersion 为 1，releases 为非空数组。每个条目包含三段或四段数字 version、yyyy-MM-dd 格式 releaseDate、官方 GitHub Release 的 releaseNotesUrl，以及 localizations。每种语言包含非空 title 与 highlights 字符串数组。建议每个版本 3–6 条，每条解释用户可感知的改进。

客户端按数字版本倒序排列，合并等价版本（例如 1.3.1 与 1.3.1.0），重复目录版本会使刷新失败。当前更新清单中的版本缺少目录条目时补为原文；已有人工译文优先。缺失语言明确标记原文，不实时机器翻译。内容以 WPF 纯文本显示；外链只允许项目官方 HTTPS Release 页面。

## 读取与测试

服务在页面打开或手动刷新时读取，启动不额外联网。先用内置快照或 %LOCALAPPDATA%/AFMediaBar/cache/release-highlights.json，再尝试 raw 与 jsDelivr。有效缓存 24 小时内复用，手动刷新绕过期限。响应限 2 MiB，每个来源 15 秒超时；无效数据、断网或失败不覆盖有效缓存。页面离开取消请求，代际检查阻止过期结果发布。

AFMEDIABAR_RELEASE_HIGHLIGHTS_URL 可替换全部来源为本地 JSON 文件、HTTPS 或回环 HTTP 测试端点。它只影响亮点目录。线上目录尚未发布时，客户端继续使用内置正式版本快照并提示刷新失败。

# Release highlights catalogue

What’s new is a read-only page. Update checks, downloads, skipped versions, and installation remain on Application and UpdateService. Maintain reviewed summaries in all three languages in the bundled JSON; run tools/prepare-release-highlights.ps1 to validate and generate a local artifact. Publication to release-metadata/release/highlights.json is a separate maintainer step and is not performed by the script.

Schema 1 contains a nonempty releases array. Each release has a numeric version, ISO date, official HTTPS GitHub release link, and localized title/highlights. Prefer 3–6 concise user-facing highlights per stable release. Existing translations take precedence over the update manifest; missing content falls back to explicitly labelled original text. The native WPF page never executes release HTML.

Opening the page reads bundled or cached content before a demand-driven refresh. Fresh cache is reused for 24 hours. Manual refresh bypasses this period; failures retain valid content. Responses are limited to 2 MiB and 15 seconds per source. The dedicated environment override can select a local file or test endpoint without changing update installation sources.
