# 从已审核的版本清单追加中文亮点，保留历史；只写本地目录，不发布 Release 或推送分支。
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$CataloguePath,
    [string]$ReleaseNotesPath,
    [switch]$ReplaceVersionText,
    [string]$Repository = 'Fervent-Tempo/AF-Media-Bar'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-notes-content.ps1')
if ($Repository -notmatch '^[\w.-]+/[\w.-]+$') { throw 'Invalid repository name.' }

function Get-NormalizedVersion([object]$Value) {
    if ($Value -isnot [string] -or $Value -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'Invalid release version.' }
    $parsed = [version]$Value
    if ($parsed.Revision -gt 0) { return $parsed.ToString(4) }
    return $parsed.ToString(3)
}

function Get-ReleaseDate([object]$Value) {
    if ($Value -isnot [string]) { throw 'Missing release date.' }
    return [datetime]::ParseExact($Value, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture).ToString('yyyy-MM-dd')
}

function Get-ChineseText([object]$Title, [object]$Highlights, [object]$Content = $null) {
    if ($Title -isnot [string] -or [string]::IsNullOrWhiteSpace($Title) -or
        $Highlights -isnot [System.Collections.IList] -or $Highlights.Count -eq 0) {
        throw 'Missing Chinese release highlights.'
    }
    $lines = @($Highlights | ForEach-Object {
        if ($_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_)) { throw 'Invalid highlight text.' }
        $_.Trim()
    })
    $text = [ordered]@{ title = $Title.Trim(); highlights = $lines }
    if ($null -ne $Content) { $text.content = Get-ValidatedReleaseHighlightsContent $Content }
    return $text
}

function Assert-NotesUrl([object]$Value, [string]$Version) {
    $prefix = 'https://github.com/' + $Repository + '/releases/tag/'
    if ($Value -isnot [string] -or $Value -notmatch ('^' + [regex]::Escape($prefix) + 'v(?<tagVersion>\d+\.\d+\.\d+(?:\.\d+)?)$') -or
        (Get-NormalizedVersion $Matches.tagVersion) -ne $Version) {
        throw 'Invalid official release notes link.'
    }
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
if ($manifest.schemaVersion -ne 1) { throw 'Unsupported update manifest.' }
$incomingVersion = Get-NormalizedVersion $manifest.version
$incomingDate = Get-ReleaseDate $manifest.releaseDate
Assert-NotesUrl $manifest.releaseNotesUrl $incomingVersion
$content = if ($manifest.Contains('releaseHighlights')) { Get-ValidatedReleaseHighlightsContent $manifest.releaseHighlights } else { $null }
$incomingText = Get-ChineseText $manifest.title $manifest.changelog $content
if ($ReleaseNotesPath) {
    $notesText = Get-ReleaseNotesText $ReleaseNotesPath $incomingVersion
    $incomingText = Get-ChineseText $notesText.title $notesText.highlights $notesText.content
}

$releases = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
if (Test-Path -LiteralPath $CataloguePath) {
    $catalogue = Get-Content -LiteralPath $CataloguePath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    if ($catalogue.schemaVersion -ne 1 -or $catalogue.releases -isnot [System.Collections.IList] -or $catalogue.releases.Count -eq 0) {
        throw 'Invalid highlights catalogue.'
    }
    foreach ($release in $catalogue.releases) {
        $version = Get-NormalizedVersion $release.version
        if (-not $seen.Add($version)) { throw "Duplicate release version: $version" }
        $date = Get-ReleaseDate $release.releaseDate
        Assert-NotesUrl $release.releaseNotesUrl $version
        $text = $release.localizations['zh-Hans']
        $content = if ($text.Contains('content')) { Get-ValidatedReleaseHighlightsContent $text.content } else { $null }
        $chinese = Get-ChineseText $text.title $text.highlights $content
        if ($ReplaceVersionText) {
            if ($version -eq $incomingVersion) {
                if ($date -ne $incomingDate -or $release.releaseNotesUrl -ne $manifest.releaseNotesUrl) {
                    throw 'Replacing text cannot change the release date or notes URL.'
                }
                $release.localizations['zh-Hans'] = $incomingText
            }
            # 回填不经过中文目录转换，其他版本、译文与附加字段均按原值保留。
            $releases.Add($release)
            continue
        }
        $releases.Add([ordered]@{
            version = $version
            releaseDate = $date
            releaseNotesUrl = $release.releaseNotesUrl
            localizations = [ordered]@{ 'zh-Hans' = $chinese }
        })
    }
}

# 重跑不覆盖已有人工摘要；版本别名也不会重复入库。
if ($seen.Add($incomingVersion)) {
    $releases.Add([ordered]@{
        version = $incomingVersion
        releaseDate = $incomingDate
        releaseNotesUrl = $manifest.releaseNotesUrl
        localizations = [ordered]@{ 'zh-Hans' = $incomingText }
    })
}
$result = [ordered]@{
    schemaVersion = 1
    releases = @($releases | Sort-Object { [version]$_.version } -Descending)
}
$json = ($result | ConvertTo-Json -Depth 20) + [Environment]::NewLine
if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 2MB) { throw 'Highlights catalogue exceeds the client size limit.' }

$target = [IO.Path]::GetFullPath($CataloguePath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
$temporary = $target + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $target, $true)
}
finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
}
Write-Output "Prepared $($result.releases.Count) Chinese releases locally; no remote state was changed."
