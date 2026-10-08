# 从已审核的版本清单追加中文亮点，保留历史；只写本地目录，不发布 Release 或推送分支。
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$CataloguePath,
    [string]$Repository = 'Fervent-Tempo/AF-Media-Bar'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
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

function Get-ChineseText([object]$Title, [object]$Highlights) {
    if ($Title -isnot [string] -or [string]::IsNullOrWhiteSpace($Title) -or
        $Highlights -isnot [System.Collections.IList] -or $Highlights.Count -eq 0) {
        throw 'Missing Chinese release highlights.'
    }
    $lines = @($Highlights | ForEach-Object {
        if ($_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_)) { throw 'Invalid highlight text.' }
        $_.Trim()
    })
    return [ordered]@{ title = $Title.Trim(); highlights = $lines }
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
$incomingText = Get-ChineseText $manifest.title $manifest.changelog

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
        $chinese = Get-ChineseText $text.title $text.highlights
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
