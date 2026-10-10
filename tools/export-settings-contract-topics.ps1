# 从机器契约和关联规则生成专题；源清单不变，避免手工同步多份字段表。
[CmdletBinding()]
param([string]$Workspace = (Split-Path -Parent $PSScriptRoot), [switch]$Check)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'settings-contract-data.ps1')
function Format-ContractCell {
    param([AllowNull()][object]$Value)
    (ConvertTo-SettingsContractJson $Value).Replace('|', '\|')
}
try {
    $workspaceRoot = (Resolve-Path -LiteralPath $Workspace).Path
    $data = Read-SettingsContractData $workspaceRoot
    $catalog = $data.Catalog
    $index = $data.Index
    foreach ($field in $catalog.fields) { $null = Get-SettingsContractGroup $index $field.path }
    foreach ($group in $index.groups) {
        $fields = @($catalog.fields | Where-Object { Test-SettingsContractSelector $group $_.path })
        $builder = [Text.StringBuilder]::new()
        $null = $builder.AppendLine("# $($group.title)设置契约")
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。')
        $null = $builder.AppendLine()
        $null = $builder.AppendLine("基线：main $($catalog.mainCommit.Substring(0, 7))；正式版 $($catalog.latestRelease)；schema $($catalog.settingsSchemaVersion)；核对日 $($catalog.verifiedDate)。")
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。')
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('## 关联规则')
        $null = $builder.AppendLine()
        $rules = @($index.rules | Where-Object {
            $rule = $_
            -not $rule.always -and -not $rule.whenEnum -and @($fields | Where-Object { Test-SettingsContractSelector $rule $_.path }).Count -gt 0
        })
        foreach ($rule in $rules) { $null = $builder.AppendLine("- $($rule.text)") }
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('## 字段')
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |')
        $null = $builder.AppendLine('|---|---|---|---|---|---|')
        foreach ($field in $fields) {
            $default = if ($field.collectionElement) { '条目模板' }
                elseif ($field.normalizedDefault -is [pscustomobject]) { '对象，见子字段' }
                else { Format-ContractCell $field.normalizedDefault }
            $missing = if ($field.onMissing.state -eq 'value' -and $field.onMissing.value -is [pscustomobject]) { '对象，查询-Details' }
                else { Format-ContractCell (ConvertTo-SettingsContractOutcome $field.onMissing) }
            $nullResult = Format-ContractCell (ConvertTo-SettingsContractOutcome $field.onExplicitNull)
            $release = if ($field.firstReleased) { $field.firstReleased } else { '**未发布**' }
            $null = $builder.AppendLine('| `' + $field.path + '` | `' + $field.type + '` | ' + $default + ' | ' + $missing + ' | ' + $nullResult + ' | ' + $release + ' |')
        }
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('## 相关枚举')
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。')
        $enumNames = @($fields.enumType | Where-Object { $_ } | Select-Object -Unique)
        foreach ($name in $enumNames) {
            $enum = $catalog.enums | Where-Object name -CEQ $name
            $null = $builder.AppendLine()
            $null = $builder.AppendLine("### $name")
            $null = $builder.AppendLine()
            $null = $builder.AppendLine("JSON无效输入回退：$($enum.invalidInputFallback)。")
            $null = $builder.AppendLine()
            $null = $builder.AppendLine('| 名称 | 数值 | 首次发布 |')
            $null = $builder.AppendLine('|---|---|---|')
            foreach ($member in $enum.members) {
                $release = if ($member.firstReleased) { $member.firstReleased } else { '**未发布**' }
                $null = $builder.AppendLine('| `' + $member.name + '` | ' + $member.value + ' | ' + $release + ' |')
            }
        }
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('源码位置、原始/发布默认和范围：用 query-settings-contracts.ps1 -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。')
        $content = $builder.ToString().Replace("`r`n", "`n").Replace("`n", "`r`n")
        $path = Join-Path $workspaceRoot "docs/settings-contracts/$($group.id).md"
        if ($Check) {
            if (-not (Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path).Replace("`r`n", "`n") -ne $content.Replace("`r`n", "`n")) {
                throw "专题内容已陈旧：$($group.id)；请重新运行导出工具。"
            }
        } else { [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false)) }
    }
    Write-Host "通过：$($index.groups.Count) 个专题$($(if ($Check) { '与机器清单一致' } else { '已生成' }))。"
    exit 0
} catch {
    Write-Error "专题导出失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
