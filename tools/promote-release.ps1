# Promotes reviewed preview metadata only after verifying and publishing its matching GitHub installer.
# The caller owns committing the stable pointer and triggering metadata delivery after the script succeeds.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$MetadataDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Tag -notmatch '^v(\d+\.\d+\.\d+(?:\.\d+)?)$') { throw 'Expected a numeric version tag.' }
$version = $Matches[1]
$previewPath = Join-Path $MetadataDirectory 'release/latest-preview.json'
$stablePath = Join-Path $MetadataDirectory 'release/latest.json'
$preview = Get-Content -LiteralPath $previewPath -Raw | ConvertFrom-Json -AsHashtable
$stable = Get-Content -LiteralPath $stablePath -Raw | ConvertFrom-Json -AsHashtable
if ($preview.version -ne $version) { throw "Reviewed preview is $($preview.version), not $version." }
if ([version]$version -lt [version]$stable.version) { throw 'Stable channel cannot be downgraded.' }
$name = "AFMediaBar-Setup-$Tag-win-x64.exe"
$url = "https://github.com/$Repository/releases/download/$Tag/$name"
$githubPackage = @($preview.packages | Where-Object { $_.url -ceq $url })
if ($githubPackage.Count -ne 1 -or $githubPackage[0].sha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw 'Preview metadata has no unique valid GitHub installer.'
}
if ($preview.schemaVersion -ne 1 -or $preview.packages.Count -eq 0) { throw 'Unsupported or empty update manifest.' }
foreach ($package in $preview.packages) {
    if ($package.sha256 -ne $githubPackage[0].sha256 -or $package.size -ne $githubPackage[0].size) {
        throw 'All mirrors must describe the same installer.'
    }
}
if ($stable.version -eq $version -and
    (Get-Content -LiteralPath $stablePath -Raw) -ne (Get-Content -LiteralPath $previewPath -Raw)) {
    throw 'An existing stable version cannot be replaced with different metadata.'
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ("afmb-promote-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    & gh release download $Tag --repo $Repository --pattern $name --dir $temp
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download the matching release installer.' }
    $installer = Get-Item -LiteralPath (Join-Path $temp $name)
    if ($installer.Length -ne $githubPackage[0].size -or
        (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash -ne $githubPackage[0].sha256) {
        throw 'Release installer does not match the reviewed metadata.'
    }
    # Publish before updating the pointer: users must never be offered a draft-only GitHub package.
    & gh release edit $Tag --repo $Repository --draft=false --prerelease=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Could not publish the stable GitHub Release.' }
    Copy-Item -LiteralPath $previewPath -Destination $stablePath -Force
}
finally {
    $resolved = [IO.Path]::GetFullPath($temp)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved).StartsWith('afmb-promote-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
