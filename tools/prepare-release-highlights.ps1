# Validates reviewed highlights and prepares a local metadata artifact; never publishes a remote file.
[CmdletBinding()]
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '../src/AFMediaBar/Resources/ReleaseHighlights.json'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '../artifacts/metadata/highlights.json'),
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$catalogue = Get-Content -LiteralPath $SourcePath -Raw | ConvertFrom-Json
if ($catalogue.schemaVersion -ne 1 -or $catalogue.releases.Count -eq 0) { throw 'Invalid highlights catalogue.' }
$seen = [System.Collections.Generic.HashSet[version]]::new()
foreach ($release in $catalogue.releases) {
    if ($release.version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'Invalid release version.' }
    $parts = $release.version.Split('.')
    $version = [version]::new([int]$parts[0], [int]$parts[1], [int]$parts[2], $(if ($parts.Length -gt 3) { [int]$parts[3] } else { 0 }))
    if (-not $seen.Add($version)) { throw "Duplicate version: $version" }
    [void][datetime]::ParseExact($release.releaseDate, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture)
    if ($release.releaseNotesUrl -notmatch '^https://github\.com/Fervent-Tempo/AF-Media-Bar/releases/tag/[^?#]+$') { throw 'Invalid official notes link.' }
    foreach ($language in @('zh-Hans', 'zh-Hant', 'en')) {
        $text = $release.localizations.$language
        if ([string]::IsNullOrWhiteSpace($text.title) -or $text.highlights.Count -eq 0) { throw "Missing $language highlights." }
        foreach ($line in $text.highlights) { if ([string]::IsNullOrWhiteSpace($line)) { throw 'Empty highlight.' } }
    }
}
if (-not $VerifyOnly) {
    [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutputPath)))
    Copy-Item -LiteralPath $SourcePath -Destination $OutputPath
}
Write-Output "Validated $($catalogue.releases.Count) releases in three languages. No remote metadata was changed."
