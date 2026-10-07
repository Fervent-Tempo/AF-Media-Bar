# 使用独立临时文件验证历史保留、版本去重与失败不覆盖；不访问网络或发布目录。
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$temp = Join-Path ([IO.Path]::GetTempPath()) ('afmb-highlights-test-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$script = Join-Path $PSScriptRoot 'update-release-highlights.ps1'
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Write-Json([string]$Path, [object]$Value) {
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
function New-Entry([string]$Version) {
    return @{
        version = $Version; releaseDate = '2026-10-01'
        releaseNotesUrl = "https://github.com/example/project/releases/tag/v$Version"
        localizations = @{
            'zh-Hans' = @{ title = '既有人工摘要'; highlights = @('保留历史内容。') }
            'en' = @{ title = 'Previous summary'; highlights = @('Old translation.') }
        }
    }
}
try {
    $manifestPath = Join-Path $temp 'manifest.json'
    $cataloguePath = Join-Path $temp 'highlights.json'
    $manifest = @{
        schemaVersion = 1; version = '1.3.0'; releaseDate = '2026-10-07'
        title = 'AF Media Bar 1.3.0'; changelog = @('新增功能。', '修复问题。')
        releaseNotesUrl = 'https://github.com/example/project/releases/tag/v1.3.0'
    }
    Write-Json $manifestPath $manifest
    Write-Json $cataloguePath @{ schemaVersion = 1; releases = @((New-Entry '1.2.0.0'), (New-Entry '1.10.0')) }
    $arguments = @{ ManifestPath = $manifestPath; CataloguePath = $cataloguePath; Repository = 'example/project' }
    & $script @arguments
    $result = Get-Content -LiteralPath $cataloguePath -Raw | ConvertFrom-Json -AsHashtable
    Assert-True (($result.releases.version -join ',') -eq '1.10.0,1.3.0,1.2.0') 'Numeric order or existing history changed.'
    Assert-True ($result.releases[2].localizations['zh-Hans'].title -eq '既有人工摘要') 'Existing manual title lost.'
    Assert-True ($result.releases[2].localizations['zh-Hans'].highlights[0] -eq '保留历史内容。') 'Historical text lost.'
    Assert-True ($result.releases[1].localizations['zh-Hans'].highlights.Count -eq 2) 'Incoming highlights lost.'
    foreach ($release in $result.releases) {
        Assert-True ($release.localizations.Count -eq 1 -and $release.localizations.Contains('zh-Hans')) 'Unexpected translation remains.'
    }
    $before = [IO.File]::ReadAllText($cataloguePath)
    & $script @arguments
    Assert-True ([IO.File]::ReadAllText($cataloguePath) -ceq $before) 'Retry is not idempotent.'
    $alias = $manifest.Clone(); $alias.version = '1.3.0.0'; $alias.title = '不能覆盖已有摘要'
    $alias.releaseNotesUrl += '.0'
    Write-Json $manifestPath $alias
    & $script @arguments
    Assert-True ([IO.File]::ReadAllText($cataloguePath) -ceq $before) 'Version alias duplicated or replaced reviewed content.'

    foreach ($change in @(
        @{ schemaVersion = 2 }, @{ version = 'invalid' }, @{ releaseDate = '2026-02-30' },
        @{ title = '' }, @{ changelog = @() }, @{ changelog = @('') },
        @{ releaseNotesUrl = 'https://github.com/other/project/releases/tag/v1.3.0' },
        @{ releaseNotesUrl = 'https://github.com/example/project/releases/tag/v1.9.0' },
        @{ version = '1.20.0'; releaseNotesUrl = 'https://github.com/example/project/releases/tag/v1.20.0'; changelog = @('中' * 750000) }
    )) {
        $invalid = $manifest.Clone()
        foreach ($key in $change.Keys) { $invalid[$key] = $change[$key] }
        Write-Json $manifestPath $invalid
        $failed = $false
        try { & $script @arguments } catch { $failed = $true }
        Assert-True $failed 'Invalid manifest accepted.'
        Assert-True ([IO.File]::ReadAllText($cataloguePath) -ceq $before) 'Failed input overwrote valid catalogue.'
    }
    Write-Json $manifestPath $manifest
    $duplicate = @{ schemaVersion = 1; releases = @((New-Entry '1.2.0'), (New-Entry '1.2.0.0')) }
    Write-Json $cataloguePath $duplicate
    $duplicateBefore = [IO.File]::ReadAllText($cataloguePath)
    $failed = $false
    try { & $script @arguments } catch { $failed = $true }
    Assert-True ($failed -and [IO.File]::ReadAllText($cataloguePath) -ceq $duplicateBefore) 'Duplicate catalogue mutated.'
    $missingChinese = New-Entry '1.2.0'; $missingChinese.localizations.Remove('zh-Hans')
    Write-Json $cataloguePath @{ schemaVersion = 1; releases = @($missingChinese) }
    $failed = $false
    try { & $script @arguments } catch { $failed = $true }
    Assert-True $failed 'Missing Chinese history was silently dropped.'
    $bootstrap = $arguments.Clone(); $bootstrap.CataloguePath = Join-Path $temp 'new/highlights.json'
    & $script @bootstrap
    $new = Get-Content -LiteralPath $bootstrap.CataloguePath -Raw | ConvertFrom-Json
    Assert-True ($new.releases.Count -eq 1 -and $new.releases[0].version -eq '1.3.0') 'Initial catalogue failed.'
    Write-Output 'Highlights append, Chinese-only conversion and failure-retention checks passed.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($temp)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved).StartsWith('afmb-highlights-test-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
