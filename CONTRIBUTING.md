# Contributing to AF Media Bar

Thank you for helping improve AF Media Bar.

## Before You Start

- Search existing Issues before opening a duplicate.
- Use the bug report form for reproducible defects and the feature form for proposals.
- Keep changes focused. Avoid unrelated formatting or refactoring in the same change.
- Security vulnerabilities must be reported through [SECURITY.md](SECURITY.md), not a public Issue.

## Development Environment

- Windows 11
- .NET 8 SDK
- PowerShell

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\AFMediaBar.csproj
dotnet build .\AFMediaBar.csproj -c Debug --no-restore
```

## Validation

Before submitting a change:

```powershell
dotnet build .\AFMediaBar.csproj -c Release --no-restore
dotnet publish .\AFMediaBar.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\AFMediaBar-win-x64
git diff --check
```

For behavior changes, verify the relevant taskbar workflow on Windows 11. Audio changes should be checked with at least one desktop player and one browser source when possible.

## Change Guidelines

- Preserve the existing WPF and Win32 ownership boundaries.
- Release COM objects, native hooks, unmanaged buffers, timers, and event subscriptions deterministically.
- Do not add telemetry or network access without an explicit design discussion and privacy documentation.
- Document user-visible changes in `CHANGELOG.md`.
- Keep public documentation in both `README.md` and `README.en-US.md` when applicable.

## Pull Requests

Describe the problem, the chosen behavior, validation performed, and any Windows-version-specific risk. Screenshots are useful for visual changes. Maintainers may request that broad changes be split into smaller submissions.
