# Generates update metadata from final release packages; never edits the live manifest.
# The caller owns publishing the packages before submitting the generated metadata for review.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Repository = 'Fervent-Tempo/AF-Media-Bar',
    [string]$ArtifactDirectory = 'artifacts',
    [string]$OutputDirectory = 'artifacts/metadata',
    [string]$ReleaseNotesPath = 'RELEASE_NOTES.md',
    [string]$SettingsPath = 'tools/release-settings.json',
    [string]$ReleaseDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+(?:\.\d+)?)$') {
    throw 'The updater requires a numeric version tag, for example v1.4.0. Preview is a channel, not a tag suffix.'
}
$version = $Matches.version
[xml]$project = Get-Content (Join-Path $PSScriptRoot '../src/AFMediaBar/AFMediaBar.csproj')
$projectVersion = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($projectVersion -ne $version) { throw "Tag $Tag does not match project version $projectVersion." }
if ($Repository -notmatch '^[\w.-]+/[\w.-]+$') { throw 'Invalid repository name.' }
$settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json -AsHashtable
if ($settings.minimumSupportedVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$' -or
    [version]$settings.minimumSupportedVersion -gt [version]$version -or
    $settings.mandatory -isnot [bool]) { throw 'Invalid update policy in release-settings.json.' }
. (Join-Path $PSScriptRoot 'release-notes-content.ps1')
$releaseText = Get-ReleaseNotesText $ReleaseNotesPath $version
$installerName = "AFMediaBar-Setup-$Tag-win-x64.exe"
$archiveName = "AFMediaBar-$Tag-win-x64.zip"
$installer = Get-Item -LiteralPath (Join-Path $ArtifactDirectory $installerName)
$archive = Get-Item -LiteralPath (Join-Path $ArtifactDirectory $archiveName)
if ($installer.Length -eq 0 -or $archive.Length -eq 0) { throw 'Release packages must not be empty.' }
$hash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$archiveHash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$githubBase = "https://github.com/$Repository/releases/download/$Tag"
$packages = @([ordered]@{ url = "$githubBase/$installerName"; size = $installer.Length; sha256 = $hash })
$manifest = [ordered]@{
    schemaVersion = 1
    version = $version
    releaseDate = ([datetime]::ParseExact($ReleaseDate, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture)).ToString('yyyy-MM-dd')
    minimumSupportedVersion = $settings.minimumSupportedVersion
    mandatory = $settings.mandatory
    title = $releaseText.title
    changelog = $releaseText.highlights
    releaseNotesUrl = "https://github.com/$Repository/releases/tag/$Tag"
    releasePageUrl = "https://github.com/$Repository/releases/latest"
    downloads = @{ github = "$githubBase/$archiveName" }
    packages = $packages
}
if ($null -ne $releaseText.content) { $manifest.releaseHighlights = $releaseText.content }
if ($settings.ContainsKey('accelerators')) { $manifest.accelerators = $settings.accelerators }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$json = $manifest | ConvertTo-Json -Depth 10
$json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'latest-preview.json') -Encoding utf8NoBOM
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'version') -Force | Out-Null
$json | Set-Content -LiteralPath (Join-Path $OutputDirectory "version/$version.json") -Encoding utf8NoBOM
@("$hash  $installerName", "$archiveHash  $archiveName") |
    Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Generated preview metadata for $Tag from final packages."
