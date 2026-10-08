# Verifies authenticated pagination and failure preservation using a local fake GitHub API.
# This script owns its temporary snapshots and restores the caller's token without making network requests.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$temp = Join-Path ([IO.Path]::GetTempPath()) ("afmb-contributors-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$previousToken = $env:GH_TOKEN
$state = @{ Requests = 0; Mode = 'paged'; Authenticated = $true }
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Invoke-RestMethod {
    param([string]$Uri, [hashtable]$Headers, [string]$Method)
    $state.Requests++
    Assert-True ($Uri -match '^https://api.github.com/repos/example/project/contributors\?per_page=100&anon=0&page=(\d+)$') 'Unexpected API endpoint.'
    $page = [int]$Matches[1]
    if ($state.Authenticated) {
        Assert-True ($Headers.Authorization -eq 'Bearer fixture-token') 'CI token missing.'
    } else {
        Assert-True (-not $Headers.ContainsKey('Authorization')) 'Unexpected local authentication.'
    }
    if ($state.Mode -eq 'empty') { return @() }
    if ($state.Mode -eq 'fail' -and $page -eq 2) { throw 'Simulated API failure.' }
    $count = if ($page -eq 1 -and $state.Mode -ne 'single') { 100 } else { 1 }
    for ($i = 0; $i -lt $count; $i++) {
        $name = "user-$page-$i"
        [pscustomobject]@{
            login = $name; contributions = $i + 1
            html_url = "https://github.com/$name"; avatar_url = "https://avatars.githubusercontent.com/$name"
        }
    }
}
function Assert-Fails([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert-True $failed $Message
}
try {
    $env:GH_TOKEN = 'fixture-token'
    $path = Join-Path $temp 'metadata/contributors.json'
    $arguments = @{ Repository = 'example/project'; OutputPath = $path }
    & "$PSScriptRoot/update-contributors.ps1" @arguments
    $snapshot = Get-Content $path -Raw | ConvertFrom-Json
    Assert-True ($snapshot.contributors.Count -eq 101 -and $state.Requests -eq 2) 'Pagination truncated contributors.'
    Assert-True ($snapshot.contributors[-1].login -eq 'user-2-0') 'Last page was lost.'
    Assert-True ($snapshot.source -notmatch 'fixture-token' -and (Get-Content $path -Raw) -match '"generatedUtc"\s*:\s*"[^"]+Z"') 'Snapshot provenance is invalid.'
    $before = [IO.File]::ReadAllBytes($path)
    $state.Mode = 'fail'
    Assert-Fails { & "$PSScriptRoot/update-contributors.ps1" @arguments } 'Partial response was accepted.'
    Assert-True ([Convert]::ToBase64String($before) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))) 'Failure overwrote the existing snapshot.'
    $state.Mode = 'empty'
    Assert-Fails { & "$PSScriptRoot/update-contributors.ps1" @arguments } 'Empty response was accepted.'
    Assert-True ([Convert]::ToBase64String($before) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))) 'Empty response overwrote the snapshot.'
    $env:GH_TOKEN = ''; $state.Authenticated = $false; $state.Mode = 'single'; $state.Requests = 0
    & "$PSScriptRoot/update-contributors.ps1" @arguments
    $snapshot = Get-Content $path -Raw | ConvertFrom-Json
    Assert-True ($snapshot.contributors.Count -eq 1 -and $state.Requests -eq 1) 'Single contributor response failed.'
    Assert-Fails { & "$PSScriptRoot/update-contributors.ps1" -Repository 'invalid' -OutputPath $path } 'Invalid repository accepted.'
    Assert-True ($state.Requests -eq 1) 'Invalid repository contacted the API.'
    Write-Host 'Contributor snapshot checks passed.'
}
finally {
    $env:GH_TOKEN = $previousToken
    $resolved = [IO.Path]::GetFullPath($temp)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved).StartsWith('afmb-contributors-test-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
