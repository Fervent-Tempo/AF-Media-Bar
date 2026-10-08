# 从 GitHub 贡献者接口生成仓库快照 docs/contributors.json。
# Generates a contributor snapshot for local use or Release CI's metadata artifact.
#
# ⚠️ 本文件必须以 **UTF-8 with BOM** 保存：Windows PowerShell 5.1 会把无 BOM 的 UTF-8 当 ANSI 读，中文注释与字符串会变成乱码并
# 直接导致语法错误（本脚本第一次提交时正是这样失败的）。改这个文件时请保持 BOM；用 pwsh（PowerShell 7）运行则没有这个问题。
# ⚠️ This file has to stay **UTF-8 with BOM**: Windows PowerShell 5.1 reads UTF-8 without a BOM as ANSI, which turns the Chinese comments and
# strings into mojibake and then into a syntax error, exactly how this script first failed. Keep the BOM when editing it; running it with pwsh
# (PowerShell 7) does not have that problem.
#
# 用途：程序优先读接口，接口被限流或不可达时回退到这份快照，因此它应当随发布更新一次。
# 用法：pwsh -NoProfile -File .\tools\update-contributors.ps1 [-Repository <owner/repo>] [-OutputPath <path>]
# Purpose: the application reads the API first and falls back to this snapshot when the API is rate-limited or unreachable, so the snapshot
# should be refreshed once per release.
# Usage: pwsh -NoProfile -File .\tools\update-contributors.ps1 [-Repository <owner/repo>] [-OutputPath <path>]
[CmdletBinding()]
param(
    [string]$Repository = 'Fervent-Tempo/AF-Media-Bar',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

if ($Repository -notmatch '^[\w.-]+/[\w.-]+$') {
    throw 'Invalid repository name; expected owner/repo.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutputPath = Join-Path $repoRoot 'docs/contributors.json'
}

$apiUrl = "https://api.github.com/repos/$Repository/contributors?per_page=100&anon=0"
Write-Host "正在读取 / fetching $apiUrl"

$headers = @{
    'User-Agent' = 'AFMediaBar-Contributors-Snapshot'
    'Accept'     = 'application/vnd.github+json'
}

# CI supplies its repository token through the environment; never print or put credentials in a URL.
if ($env:GH_TOKEN) {
    $headers.Authorization = "Bearer $env:GH_TOKEN"
}
$response = @()
$page = 1
do {
    $result = Invoke-RestMethod -Uri "$apiUrl&page=$page" -Headers $headers -Method Get
    $items = @()
    if ($null -ne $result) { $items = @($result) }
    $response += $items
    $page++
} while ($items.Count -eq 100)
if ($response.Count -eq 0) {
    throw "接口没有返回贡献者 / the API returned no contributors；确认仓库地址是否正确、是否触发了限流（未认证 60 次/小时）"
}

# 只保留界面用到的字段，且字段名与接口一致：程序读快照用的是同一个解析器。
# Only the fields the interface uses are kept, under the API's own names, because the application parses the snapshot with the same parser.
$contributors = @(foreach ($item in $response) {
    [ordered]@{
        login         = $item.login
        contributions = $item.contributions
        html_url      = $item.html_url
        avatar_url    = $item.avatar_url
    }
})

$snapshot = [ordered]@{
    generatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    source       = $apiUrl
    contributors = @($contributors)
}

$json = $snapshot | ConvertTo-Json -Depth 5
New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))) -Force | Out-Null
Set-Content -Path $OutputPath -Value $json -Encoding UTF8

Write-Host "已写入 / wrote $OutputPath（$($contributors.Count) 位贡献者 / contributors）"
