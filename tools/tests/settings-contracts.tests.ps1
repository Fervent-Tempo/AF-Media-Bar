# 验证查询只返回目标及关联信息，保留数组/发布状态，并拒绝宽查询和未知项；不启动应用。
[CmdletBinding()]
param([string]$Workspace = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference = 'Stop'
function Assert-ContractQuery {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}
function Invoke-ContractTool {
    param([string[]]$Arguments, [string]$Failure, [string]$Action = 'Query')
    $output = & pwsh -NoProfile -File (Join-Path (Split-Path -Parent $PSScriptRoot) 'settings-contracts.ps1') -Action $Action -Workspace $Workspace @Arguments 2>&1
    $code = $LASTEXITCODE
    $text = $output -join "`n"
    if ($Failure) {
        Assert-ContractQuery ($code -eq 1 -and $text -match $Failure) "查询未按预期拒绝：$Failure"
        Assert-ContractQuery ($text.Length -lt 600) '拒绝宽查询时不应输出完整清单。'
        return $null
    }
    Assert-ContractQuery ($code -eq 0) "查询失败：$text"
    $data = if ($Action -eq 'Query') { $text | ConvertFrom-Json } else { $null }
    [pscustomobject]@{ Data = $data; Bytes = [Text.Encoding]::UTF8.GetByteCount($text); Text = $text }
}
try {
    $cover = Invoke-ContractTool -Arguments @('-Path', 'settings.interaction.artworkHoverMode')
    Assert-ContractQuery (@($cover.Data.fields).Count -eq 1) '封面查询返回了无关字段。'
    Assert-ContractQuery ($cover.Data.fields[0].status -eq 'unreleased' -and $cover.Data.fields[0].default -eq 'Off') '封面发布状态或默认值改变。'
    Assert-ContractQuery (@($cover.Data.enums).Count -eq 1 -and @($cover.Data.enums[0].members).Count -eq 3) '封面查询缺少相关枚举。'
    Assert-ContractQuery ('artwork-hover' -in $cover.Data.rules.id -and 'interaction' -in $cover.Data.rules.id) '封面查询缺少行为或重置规则。'
    Assert-ContractQuery ('settings.taskbarExperience.artworkVisible' -in $cover.Data.relatedPaths) '封面查询丢失关联条件。'
    Assert-ContractQuery ($cover.Bytes -le 3072 -and -not $cover.Text.Contains('sourceFiles')) '单字段默认查询超过3KB或读入了无关证据。'

    $language = Invoke-ContractTool -Arguments @('-Path', 'settings.interfaceLanguage')
    $vietnamese = $language.Data.enums[0].members | Where-Object name -EQ 'Vietnamese'
    Assert-ContractQuery ($language.Data.fields[0].status -eq 'released' -and $null -eq $vietnamese.release) '父字段与新增枚举成员的发布状态被混同。'

    $metrics = Invoke-ContractTool -Arguments @('-Path', 'settings.performanceComponent.metrics')
    Assert-ContractQuery ($metrics.Data.fields[0].default -is [array] -and @($metrics.Data.fields[0].missing).Count -eq 1) '单项数组被压成标量。'

    $panel = Invoke-ContractTool -Arguments @('-Path', 'settings.taskbarExperience.fullPanel.mediaInfoVisible')
    Assert-ContractQuery ('full-panel' -in $panel.Data.rules.id -and @($panel.Data.relatedPaths).Count -eq 3) '完整面板查询丢失四开关联动。'

    $old = Invoke-ContractTool -Arguments @('-Path', 'settings.lyricsMatchStrictness')
    Assert-ContractQuery (@($old.Data.fields).Count -eq 0 -and $old.Data.removed[0].firstReleased -eq 'v1.2.0') '历史删除项被误判为未发布。'
    Assert-ContractQuery (@($old.Data.rules).Count -eq 1 -and $old.Data.rules[0].id -eq 'removed-strictness') '历史查询混入了当前字段规则。'

    $enum = Invoke-ContractTool -Arguments @('-Enum', 'ArtworkHoverMode')
    Assert-ContractQuery (@($enum.Data.fields).Count -eq 0 -and @($enum.Data.rules).Count -eq 1) '纯枚举查询混入字段规则。'
    $detail = Invoke-ContractTool -Arguments @('-Path', 'settings.appearance.fontWeight', '-Details')
    Assert-ContractQuery ($detail.Data.fields[0].latestReleaseDefault -eq 400 -and $detail.Data.fields[0].missing -eq 100) '详细查询丢失已核对的缺字段差异。'
    $prefix = Invoke-ContractTool -Arguments @('-Prefix', 'settings.taskbarExperience.hoverControls')
    Assert-ContractQuery (@($prefix.Data.fields).Count -eq 6) '限定前缀没有完整返回匹配项。'
    $list = Invoke-ContractTool -Arguments @('-List')
    $groupPathCount = ($list.Data.groups | ForEach-Object { $_.count } | Measure-Object -Sum).Sum
    Assert-ContractQuery (@($list.Data.groups).Count -eq 8 -and $groupPathCount -eq 138) '专题索引覆盖不完整。'

    Invoke-ContractTool -Arguments @('-Prefix', 'settings') -Failure '超过8项' | Out-Null
    Invoke-ContractTool -Arguments @('-Path', 'settings.notRegistered') -Failure '不能据此视为未发布' | Out-Null
    Invoke-ContractTool -Arguments @('-Enum', 'NotRegistered') -Failure '清单无此枚举' | Out-Null
    Invoke-ContractTool -Arguments @('-Path', 'settings.position', '-Enum', 'ArtworkHoverMode') -Failure '只接受Path' | Out-Null
    Invoke-ContractTool -Action Generate -Arguments @('-Details') -Failure '不接受查询参数' | Out-Null
    Invoke-ContractTool -Action Verify -Arguments @('-Check') -Failure 'Check仅用于Generate' | Out-Null
    $topicFiles = Get-ChildItem -LiteralPath (Join-Path $Workspace 'docs/settings-contracts') -File -Filter '*.md'
    $timestampsBefore = @($topicFiles | Sort-Object Name | ForEach-Object { $_.LastWriteTimeUtc.Ticks })
    $generateCheck = Invoke-ContractTool -Action Generate -Arguments @('-Check')
    Assert-ContractQuery ($generateCheck.Text -match '8 个专题与机器清单一致') 'Generate -Check 未验证所有专题。'
    $verify = Invoke-ContractTool -Action Verify
    Assert-ContractQuery ($verify.Text -match '138 个配置路径') 'Verify 未完整校验清单。'
    $timestampsAfter = @(Get-ChildItem -LiteralPath (Join-Path $Workspace 'docs/settings-contracts') -File -Filter '*.md' | Sort-Object Name | ForEach-Object { $_.LastWriteTimeUtc.Ticks })
    Assert-ContractQuery (-not (Compare-Object $timestampsBefore $timestampsAfter)) '只读校验改写了专题文件。'
    Write-Host "通过：9类查询、6类拒绝、Generate/Verify只读校验；封面摘要$($cover.Bytes)字节。"
    exit 0
} catch {
    Write-Error "契约查询验证失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
