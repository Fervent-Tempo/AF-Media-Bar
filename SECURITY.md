# 安全策略 / Security Policy

## 支持的版本 / Supported Versions

| 版本 / Version | 是否支持 / Supported |
| --- | --- |
| 1.x | 是 / Yes |
| 更早的版本 / Earlier versions | 否 / No |

## 报告安全漏洞 / Reporting a Vulnerability

请使用本仓库的 GitHub 私密漏洞报告功能：

Please use GitHub's private vulnerability reporting for this repository:

https://github.com/Fervent-Tempo/AF-Media-Bar/security/advisories/new

请勿在公开 Issue 中包含漏洞利用细节、用户隐私信息或未公开的概念验证代码。如果私密漏洞报告功能不可用，请创建一个不包含敏感细节的简短 Issue，向维护者索取私密联系方式。

Do not include exploit details, private user information, or unpublished proof-of-concept code in a public Issue. If private vulnerability reporting is unavailable, open a minimal Issue asking the maintainer for a private contact channel without disclosing sensitive details.

请提供受影响的 AF Media Bar 版本、Windows 版本、影响范围、复现条件及可行的缓解建议。我们会尽力审查报告；确认问题后，将通过私密渠道协调处理，直至修复或缓解措施可用。

Include the affected AF Media Bar version, Windows version, impact, reproduction conditions, and any proposed mitigation. Reports will be reviewed on a best-effort basis; a confirmed issue will be coordinated privately until a fix or mitigation is available.

## 安全范围说明 / Scope Notes

AF Media Bar 以当前用户身份运行，并与本地 Windows 媒体、音频、注册表和任务栏 API 交互。项目不包含网络服务、遥测客户端、更新下载器或 Explorer 注入组件。

AF Media Bar runs as the current user and interacts with local Windows media, audio, registry, and taskbar APIs. It does not include a network service, telemetry client, update downloader, or Explorer injection component.
