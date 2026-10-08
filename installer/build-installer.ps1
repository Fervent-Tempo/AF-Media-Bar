# AF Media Bar 安装包构建脚本 / AF Media Bar installer build script
#
# 本地与 CI 共用同一条链路：读取项目版本 -> 单文件自包含发布 -> Inno Setup 编译 -> 校验产物与哈希。
# The local and CI paths are the same chain: read the project version, publish the self-contained single
# file, compile with Inno Setup, then verify the artifact and its hash.
#
# 版本号只有一个来源（AFMediaBar.csproj 的 <Version>），因此安装包不可能与程序集版本不一致。
# The version has exactly one source (the <Version> property in AFMediaBar.csproj), so the installer can
# never disagree with the assembly version.

[CmdletBinding()]
param(
    # 覆盖版本号；默认从项目文件读取。 / Overrides the version; read from the project file by default.
    [string]$Version,

    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',

    # 单文件发布目录，与 AFMediaBar.iss 的 PublishDir 对应。 / Single-file publish directory, matching PublishDir in AFMediaBar.iss.
    [string]$PublishDir = 'artifacts\publish',

    # 安装包输出目录，与 AFMediaBar.iss 的 OutputDir 对应。 / Installer output directory, matching OutputDir in AFMediaBar.iss.
    [string]$OutputDir = 'artifacts',

    # Inno Setup 编译器路径；留空时按 PATH、Program Files 顺序查找。 / Inno Setup compiler path; searched in PATH and Program Files when omitted.
    [string]$IsccPath,

    # 复用已存在的发布目录，不再调用 dotnet publish。 / Reuses an existing publish directory instead of running dotnet publish.
    [switch]$SkipPublish,

    # 找不到编译器时尝试用 Chocolatey 安装 Inno Setup（CI 使用）。 / Tries installing Inno Setup through Chocolatey when the compiler is missing (CI).
    [switch]$EnsureIscc,

    # 跳过 restore（要求调用方已经用同一个 RID restore 过）。 / Skips restore (the caller must already have restored for the same RID).
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\AFMediaBar\AFMediaBar.csproj'
$solutionPath = Join-Path $repoRoot 'src\AFMediaBar.slnx'
$issPath = Join-Path $PSScriptRoot 'AFMediaBar.iss'
$publishPath = Join-Path $repoRoot $PublishDir
$outputPath = Join-Path $repoRoot $OutputDir
$exeName = 'AFMediaBar.exe'

function Invoke-DotNet {
    param([string[]]$Arguments)

    Write-Host "> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Resolve-Version {
    if ($Version) {
        return $Version
    }

    # -getProperty 需要 .NET 8 及以上的 SDK；仓库的 global.json 已固定在受支持的版本带上。
    # -getProperty requires SDK 8 or newer; the repository's global.json already pins a supported band.
    $resolved = (& dotnet msbuild $projectPath -getProperty:Version -nologo)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the project version through dotnet msbuild.'
    }

    $value = @($resolved | Where-Object { $_ -and $_.ToString().Trim() })[-1].ToString().Trim()
    if (-not $value) {
        throw 'The project file does not declare a <Version> property.'
    }

    return $value
}

function Resolve-Iscc {
    if ($IsccPath) {
        if (-not (Test-Path -LiteralPath $IsccPath)) {
            throw "ISCC.exe was not found at '$IsccPath'."
        }
        return (Resolve-Path -LiteralPath $IsccPath).Path
    }

    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    # 只在变量存在时参与拼接：32 位 Windows 上 ProgramFiles(x86) 未定义。
    # Only joined when the variable exists: ProgramFiles(x86) is undefined on 32-bit Windows.
    $candidates = @()
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if ($root) {
            $candidates += (Join-Path $root 'Inno Setup 6\ISCC.exe')
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    if ($EnsureIscc -and (Get-Command 'choco.exe' -ErrorAction SilentlyContinue)) {
        Write-Host '> choco install innosetup -y --no-progress'
        & choco install innosetup -y --no-progress
        if ($LASTEXITCODE -ne 0) {
            throw "Chocolatey could not install Inno Setup (exit code $LASTEXITCODE)."
        }
        return Resolve-Iscc
    }

    throw @'
Inno Setup 6 was not found. Install it once with either:
  winget install --id JRSoftware.InnoSetup -e
  choco install innosetup -y
then rerun this script (or pass -IsccPath <path to ISCC.exe>, or -EnsureIscc on a runner with Chocolatey).
'@
}

function Resolve-ChineseMessagesFile {
    <#
    .SYNOPSIS
    找到简体中文的语言文件，并返回它的完整路径。
    Locates the Simplified Chinese messages file and returns its full path.

    .DESCRIPTION
    简体中文属于 Inno Setup 的"非官方语言包"：官方安装程序、winget 与 choco 都不带它，因此把 MessagesFile 写成
    "compiler:Languages\ChineseSimplified.isl" 在 CI 上必然失败（真实发生过）。查找顺序：
      1. 仓库缓存 installer\languages\ChineseSimplified.isl —— 该文件随仓库分发，因此 CI 与离线构建都不需要网络；
      2. 本机 Inno Setup 安装目录（有人手工装过语言包时直接用）；
      3. 从上游 kira-96/Inno-Setup-Chinese-Simplified-Translation 下载到该缓存目录（raw 优先、jsDelivr 备用）。

    被采用的每一份文件都必须与 $expectedSha256 一致：语言文件决定向导里全部内建文案，来源不明或被改过的文件宁可让
    构建失败，也不要产出一个文案不对的安装包。
    Simplified Chinese lives in Inno Setup's "unofficial languages" set, which neither the official installer nor winget nor
    choco ships, so writing MessagesFile as "compiler:Languages\ChineseSimplified.isl" always fails on CI (it really happened).
    The order is the repository cache (shipped with the repository, so CI and offline builds need no network), then the local Inno
    installation, then a download from the upstream kira-96 repository into that cache (raw first, jsDelivr as the fallback).
    Every adopted file must match $expectedSha256: the language file decides every built-in string in the wizard, so a file of
    unknown provenance fails the build instead of producing an installer whose wording is wrong.
    #>
    param([string]$IsccPath)

    $cacheDirectory = Join-Path $PSScriptRoot 'languages'
    $cachePath = Join-Path $cacheDirectory 'ChineseSimplified.isl'

    # 随仓库分发的那一份：CI 与离线构建因此完全不依赖网络。换上游版本时必须同一次改动里更新这里的期望哈希。
    # The copy that ships with the repository, so CI and offline builds never depend on the network. Bumping the upstream
    # version means updating the expected hash here in the same change.
    $expectedSha256 = 'bf0751fa176569c6faa2f6e17ed2734617bef325d5cc06eae030fdd0258ee778'

    function Test-MessagesFile([string]$Path) {
        $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -eq $expectedSha256) { return $true }
        Write-Warning "Chinese messages file $Path has SHA-256 $actual but $expectedSha256 was expected."
        return $false
    }

    if (Test-Path -LiteralPath $cachePath) {
        if (Test-MessagesFile $cachePath) {
            Write-Host "Chinese messages file: $cachePath (repository cache)"
            return (Resolve-Path -LiteralPath $cachePath).Path
        }

        # 被改过的缓存文件不能继续用：继续用会让向导文案与预期不符，而失败的构建至少能指出是哪一份文件不对。
        # A modified cache file is not used: it would change the wizard wording, while a failed build at least says which file is
        # wrong. The message deliberately does not fall through to the download, because overwriting a file that a human edited
        # without being asked to would hide the real situation.
        throw @"
The Simplified Chinese messages file in the repository cache does not match the expected content:
  $cachePath
Expected SHA-256 $expectedSha256
Restore the shipped file with: git checkout -- installer/languages/ChineseSimplified.isl
"@
    }

    # 本机 Inno 安装目录里可能已有该文件（例如用户自己装过语言包）。
    # The local Inno installation may already carry the file (a user who installed the language pack by hand).
    $installed = Join-Path (Split-Path -Parent $IsccPath) 'Languages\ChineseSimplified.isl'
    if ((Test-Path -LiteralPath $installed) -and (Test-MessagesFile $installed)) {
        Write-Host "Chinese messages file: $installed (installed Inno Setup)"
        return (Resolve-Path -LiteralPath $installed).Path
    }

    # 上游是简体中文翻译的维护仓库（Inno Setup 官方只登记、不分发）。换地址前先确认它给出的仍是同一份文件。
    # Upstream is the repository that maintains this translation (Inno Setup lists it but does not ship it). Confirm a new address
    # still serves the very same file before replacing these.
    $sources = @(
        'https://raw.githubusercontent.com/kira-96/Inno-Setup-Chinese-Simplified-Translation/main/ChineseSimplified.isl'
        'https://cdn.jsdelivr.net/gh/kira-96/Inno-Setup-Chinese-Simplified-Translation@main/ChineseSimplified.isl'
    )

    New-Item -ItemType Directory -Force -Path $cacheDirectory | Out-Null
    foreach ($source in $sources) {
        try {
            Write-Host "> downloading Chinese messages file from $source"
            $temporary = "$cachePath.tmp"
            Invoke-WebRequest -Uri $source -OutFile $temporary -UseBasicParsing
            if (-not (Test-Path -LiteralPath $temporary) -or (Get-Item -LiteralPath $temporary).Length -lt 1024) {
                throw 'the downloaded file is missing or implausibly small.'
            }

            if (-not (Test-MessagesFile $temporary)) {
                throw "the downloaded file is not the expected translation."
            }

            Move-Item -LiteralPath $temporary -Destination $cachePath -Force
            Write-Host "Chinese messages file: $cachePath (downloaded, SHA-256 $expectedSha256)"
            return (Resolve-Path -LiteralPath $cachePath).Path
        }
        catch {
            Write-Warning "Could not fetch the Chinese messages file from $source : $($_.Exception.Message)"
        }
    }

    throw @"
The Simplified Chinese messages file could not be located or downloaded.

Installers compiled without it would show an English wizard, which is why this fails instead of continuing.
The file normally ships with the repository; restore it with:
  git checkout -- installer/languages/ChineseSimplified.isl
Otherwise place it manually at:
  $cachePath
by copying it from an Inno Setup installation that has the language pack
(<Inno Setup>\Languages\ChineseSimplified.isl), or by downloading it from:
  https://raw.githubusercontent.com/kira-96/Inno-Setup-Chinese-Simplified-Translation/main/ChineseSimplified.isl
The file must have SHA-256 $expectedSha256.
"@
}

$appVersion = Resolve-Version
Write-Host "AF Media Bar version: $appVersion"

# 先找编译器再发布：缺工具的失败应当是即时的，而不是等完整发布跑完才报错。
# The compiler is located before publishing so a missing tool fails immediately instead of after a full publish.
$iscc = Resolve-Iscc
$chineseMessagesFile = Resolve-ChineseMessagesFile -IsccPath $iscc

if (-not $SkipPublish) {
    if (-not $NoRestore) {
        # restore 与 publish 必须使用同一个 RID，否则单文件发布会在 NETSDK1047 上失败。
        # restore and publish must use the same RID, otherwise single-file publish fails with NETSDK1047.
        Invoke-DotNet @('restore', $solutionPath, '-r', $RuntimeIdentifier)
    }

    New-Item -ItemType Directory -Force -Path $publishPath | Out-Null
    Invoke-DotNet @(
        'publish', $projectPath,
        '-c', $Configuration,
        '-r', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        '-p:BuildInParallel=false',
        '-o', $publishPath
    )
}

# 发布模式一旦退化（例如丢失 PublishSingleFile）就会在安装包里塞进整个运行时，这里提前失败。
# A regressed publish mode (a lost PublishSingleFile, for example) would stuff the whole runtime into the
# installer, so the build fails here instead.
if (-not (Test-Path -LiteralPath $publishPath)) {
    throw "Publish directory '$publishPath' does not exist. Run without -SkipPublish first."
}
$publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne $exeName) {
    throw "Expected exactly one file named $exeName in '$publishPath', found: $($publishedFiles.Name -join ', ')."
}

Write-Host "Inno Setup compiler: $iscc"

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
Write-Host "> ISCC.exe /DMyAppVersion=$appVersion /DPublishDir=$publishPath /DChineseMessagesFile=$chineseMessagesFile"
& $iscc "/DMyAppVersion=$appVersion" "/DPublishDir=$publishPath" "/DChineseMessagesFile=$chineseMessagesFile" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$installerName = "AFMediaBar-Setup-v$appVersion-$RuntimeIdentifier.exe"
$installerPath = Join-Path $outputPath $installerName
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "ISCC reported success but '$installerPath' is missing."
}

$installer = Get-Item -LiteralPath $installerPath
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $outputPath 'installer-sha256.txt'
"$hash  $installerName" | Set-Content -LiteralPath $checksumPath -Encoding ascii

$sizeMb = [Math]::Round($installer.Length / 1MB, 1)
Write-Host ''
Write-Host "Installer: $installerPath"
Write-Host "Size:      $sizeMb MB"
Write-Host "SHA-256:   $hash"
Write-Host "Checksum:  $checksumPath"
Write-Host ''
Write-Host 'Release CI generates metadata from the final packages with tools/new-release-manifest.ps1.'
