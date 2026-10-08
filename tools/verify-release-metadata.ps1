# Exercises manifest integrity and promotion failure boundaries with local fixtures and a fake GitHub CLI.
# No release, branch or external service is changed; this script owns and removes its temporary fixtures.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
$temp = Join-Path ([IO.Path]::GetTempPath()) ("afmb-release-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Fails([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert-True $failed $Message
}
$testState = @{ PublishCount = 0; DownloadCount = 0; FailPublish = $false; CorruptDownload = $false; Installer = '' }
function gh {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $global:LASTEXITCODE = 0
    if ($Arguments[0] -eq 'release' -and $Arguments[1] -eq 'download') {
        $testState.DownloadCount++
        $directory = $Arguments[[array]::IndexOf($Arguments, '--dir') + 1]
        Copy-Item -LiteralPath $testState.Installer -Destination $directory
        if ($testState.CorruptDownload) {
            Add-Content -LiteralPath (Join-Path $directory (Split-Path $testState.Installer -Leaf)) -Value 'corruption'
        }
    } elseif ($Arguments[0] -eq 'release' -and $Arguments[1] -eq 'edit') {
        $testState.PublishCount++
        if ($testState.FailPublish) { $global:LASTEXITCODE = 1 }
    } else { throw "Unexpected mocked gh invocation: $Arguments" }
}
try {
    [xml]$project = Get-Content (Join-Path $repoRoot 'src/AFMediaBar/AFMediaBar.csproj')
    $version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    $tag = "v$version"
    $installerName = "AFMediaBar-Setup-$tag-win-x64.exe"
    $archiveName = "AFMediaBar-$tag-win-x64.zip"
    $testState.Installer = Join-Path $temp $installerName
    [IO.File]::WriteAllBytes($testState.Installer, [byte[]](1, 2, 3, 4, 5))
    [IO.File]::WriteAllBytes((Join-Path $temp $archiveName), [byte[]](6, 7, 8))
    $notes = Join-Path $temp 'notes.md'
    @("# AF Media Bar $version", '', 'Release highlights.', '- Fixed a regression.', '## 下载与安装', 'Do not include this.') |
        Set-Content $notes -Encoding utf8NoBOM
    $settings = Join-Path $temp 'settings.json'
    '{"minimumSupportedVersion":"1.0.0","mandatory":false,"accelerators":[]}' | Set-Content $settings
    $output = Join-Path $temp 'generated'
    $arguments = @{
        Tag = $tag; Repository = 'example/project'; ArtifactDirectory = $temp; OutputDirectory = $output
        ReleaseNotesPath = $notes; SettingsPath = $settings; ReleaseDate = '2026-10-03'
    }
    & "$PSScriptRoot/new-release-manifest.ps1" @arguments
    $previewPath = Join-Path $output 'latest-preview.json'
    $manifest = Get-Content $previewPath -Raw | ConvertFrom-Json -AsHashtable
    Assert-True ($manifest.version -eq $version -and $manifest.schemaVersion -eq 1) 'Version/schema changed.'
    Assert-True ($manifest.packages.Count -eq 1 -and $manifest.packages[0].url -eq "https://github.com/example/project/releases/download/$tag/$installerName") 'Expected one GitHub installer.'
    Assert-True ($manifest.packages[0].sha256 -eq (Get-FileHash $testState.Installer).Hash.ToLowerInvariant()) 'Installer hash mismatch.'
    Assert-True ($manifest.packages[0].size -eq 5) 'Installer byte count mismatch.'
    Assert-True ($manifest.ContainsKey('accelerators') -and $manifest.accelerators.Count -eq 0) 'Explicit disabled accelerators lost.'
    Assert-True ($manifest.changelog.Count -eq 2 -and $manifest.changelog -notcontains 'Do not include this.') 'Highlights extraction failed.'
    Assert-True ((Get-Content (Join-Path $temp 'SHA256SUMS.txt')).Count -eq 2) 'Missing package checksums.'
    Assert-True ((Get-Content (Join-Path $output "version/$version.json") -Raw) -eq (Get-Content $previewPath -Raw)) 'Version snapshot differs.'
    $invalid = $arguments.Clone(); $invalid.Tag = "$tag-preview"
    Assert-Fails { & "$PSScriptRoot/new-release-manifest.ps1" @invalid } 'Unsupported version accepted.'
    $invalid = $arguments.Clone(); $invalid.Tag = 'v99.99.99'
    Assert-Fails { & "$PSScriptRoot/new-release-manifest.ps1" @invalid } 'Mismatched project version accepted.'
    $invalid = $arguments.Clone(); $invalid.ReleaseDate = 'invalid'
    Assert-Fails { & "$PSScriptRoot/new-release-manifest.ps1" @invalid } 'Invalid release date accepted.'
    $invalid = $arguments.Clone(); $invalid.ReleaseNotesPath = $settings
    Assert-Fails { & "$PSScriptRoot/new-release-manifest.ps1" @invalid } 'Stale release notes accepted.'

    $metadata = Join-Path $temp 'metadata'
    New-Item -ItemType Directory -Path (Join-Path $metadata 'release') | Out-Null
    Copy-Item $previewPath (Join-Path $metadata 'release/latest-preview.json')
    $stablePath = Join-Path $metadata 'release/latest.json'
    '{"version":"1.0.0"}' | Set-Content $stablePath
    $stableBefore = Get-Content $stablePath -Raw
    $promote = @{ Tag = $tag; Repository = 'example/project'; MetadataDirectory = $metadata }
    $invalid = $promote.Clone(); $invalid.Tag = 'v99.99.99'
    Assert-Fails { & "$PSScriptRoot/promote-release.ps1" @invalid } 'An unreviewed version was promoted.'
    Assert-True ($testState.DownloadCount -eq 0) 'Invalid version contacted GitHub.'
    $testState.CorruptDownload = $true
    Assert-Fails { & "$PSScriptRoot/promote-release.ps1" @promote } 'Corrupted installer promoted.'
    Assert-True ($testState.PublishCount -eq 0 -and (Get-Content $stablePath -Raw) -eq $stableBefore) 'Failed integrity check changed stable state.'
    $testState.CorruptDownload = $false; $testState.FailPublish = $true
    Assert-Fails { & "$PSScriptRoot/promote-release.ps1" @promote } 'Failed publication was accepted.'
    Assert-True ((Get-Content $stablePath -Raw) -eq $stableBefore) 'Failed publication changed stable pointer.'
    $testState.FailPublish = $false
    & "$PSScriptRoot/promote-release.ps1" @promote
    Assert-True ((Get-Content $stablePath -Raw) -eq (Get-Content $previewPath -Raw)) 'Successful promotion lost metadata.'
    & "$PSScriptRoot/promote-release.ps1" @promote
    '{"version":"99.99.99"}' | Set-Content $stablePath
    Assert-Fails { & "$PSScriptRoot/promote-release.ps1" @promote } 'Stable downgrade accepted.'
    Write-Host 'Release metadata generation and promotion checks passed.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($temp)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved).StartsWith('afmb-release-test-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
