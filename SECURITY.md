# Security Policy

## Supported Versions

| Version | Supported |
| --- | --- |
| 1.x | Yes |
| Earlier versions | No |

## Reporting a Vulnerability

Please use GitHub's private vulnerability reporting for this repository:

https://github.com/Fervent-Tempo/AF-Media-Bar/security/advisories/new

Do not include exploit details, private user information, or unpublished proof-of-concept code in a public Issue. If private vulnerability reporting is unavailable, open a minimal Issue asking the maintainer for a private contact channel without disclosing sensitive details.

Include the affected AF Media Bar version, Windows version, impact, reproduction conditions, and any proposed mitigation. Reports will be reviewed on a best-effort basis; a confirmed issue will be coordinated privately until a fix or mitigation is available.

## Scope Notes

AF Media Bar runs as the current user and interacts with local Windows media, audio, registry, and taskbar APIs. It does not include a network service, telemetry client, update downloader, or Explorer injection component.
