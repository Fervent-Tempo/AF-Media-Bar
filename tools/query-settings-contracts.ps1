# 按字段或枚举返回有限摘要；宽查询须显式选择，未知项不按未发布处理。
[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Path')][ValidateNotNullOrEmpty()][string[]]$Path,
    [Parameter(Mandatory, ParameterSetName = 'Prefix')][ValidateNotNullOrEmpty()][string]$Prefix,
    [Parameter(Mandatory, ParameterSetName = 'Enum')][ValidateNotNullOrEmpty()][string[]]$Enum,
    [Parameter(ParameterSetName = 'List')][switch]$List,
    [switch]$Details,
    [ValidateRange(1, 32)][int]$MaxFields = 8,
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'settings-contract-data.ps1')
try {
    $workspaceRoot = (Resolve-Path -LiteralPath $Workspace).Path
    $data = Read-SettingsContractData $workspaceRoot
    $catalog = $data.Catalog
    $index = $data.Index
    $result = [ordered]@{ release = $catalog.latestRelease; schema = $catalog.settingsSchemaVersion; verified = $catalog.verifiedDate; codeBase = $catalog.mainCommit.Substring(0, 7) }
    if ($PSCmdlet.ParameterSetName -eq 'List') {
        $result.groups = @($index.groups | ForEach-Object {
            $group = $_
            [ordered]@{ id = $group.id; title = $group.title; count = @($catalog.fields | Where-Object { Test-SettingsContractSelector $group $_.path }).Count; document = "docs/settings-contracts/$($group.id).md" }
        })
        $result.usage = '-Path 精确JSON路径；-Prefix 前缀最多8项；-Enum 类型名；-Details 增补原始/发布默认与范围。'
    } else {
        $selected = @()
        $removed = @()
        if ($PSCmdlet.ParameterSetName -eq 'Path') {
            foreach ($requested in ($Path | Select-Object -Unique)) {
                $field = @($catalog.fields | Where-Object path -CEQ $requested)
                $old = @($catalog.removedReleasedFields | Where-Object path -CEQ $requested)
                if ($field.Count) { $selected += $field }
                elseif ($old.Count) { $removed += $old }
                else { throw "清单无此路径，不能据此视为未发布：$requested" }
            }
        } elseif ($PSCmdlet.ParameterSetName -eq 'Prefix') {
            $selected = @($catalog.fields | Where-Object { $_.path.StartsWith($Prefix, [StringComparison]::Ordinal) })
            if (-not $selected.Count) { throw "前缀没有匹配路径：$Prefix" }
        }
        if ($selected.Count + $removed.Count -gt $MaxFields) { throw "匹配$($selected.Count + $removed.Count)项，超过$($MaxFields)项；缩小范围，或显式提高-MaxFields（最多32）。" }
        $result.fields = @($selected | ForEach-Object {
            $field = $_
            $group = Get-SettingsContractGroup $index $field.path
            $default = $field.normalizedDefault
            if ($field.collectionElement) { $default = '列表条目模板；无默认条目' }
            elseif ($field.normalizedDefault -is [pscustomobject]) { $default = '对象；查询子路径' }
            $missing = ConvertTo-SettingsContractOutcome $field.onMissing
            if ($field.onMissing.state -eq 'value' -and $field.onMissing.value -is [pscustomobject]) { $missing = '对象；-Details查看读取结果' }
            $row = [ordered]@{ path = $field.path; type = $field.type; release = $field.firstReleased; status = $field.releaseStatus; default = $default; missing = $missing; null = (ConvertTo-SettingsContractOutcome $field.onExplicitNull); source = "$($field.source):$($field.sourceLine)"; document = "docs/settings-contracts/$($group.id).md" }
            if ($Details) {
                $row.nullable = $field.nullable
                $row.rawDefault = $field.rawDefault
                $row.latestReleaseDefault = $field.latestReleaseDefault
                $row.readEvidence = [ordered]@{ missing = $field.onMissing; null = $field.onExplicitNull }
                $row.limits = $catalog.limits.($field.declaringType)
            }
            $row
        })
        if ($removed.Count) { $result.removed = $removed }
        $enumNames = if ($PSCmdlet.ParameterSetName -eq 'Enum') { @($Enum | Select-Object -Unique) } else { @($selected.enumType | Where-Object { $_ } | Select-Object -Unique) }
        if ($enumNames.Count -gt $MaxFields) { throw '枚举查询超过输出上限。' }
        $result.enums = @($enumNames | ForEach-Object {
            $name = $_
            $definition = @($catalog.enums | Where-Object name -CEQ $name)
            if ($definition.Count -ne 1) { throw "清单无此枚举：$name" }
            [ordered]@{ name = $name; invalidInputFallback = $definition[0].invalidInputFallback; members = @($definition[0].members | ForEach-Object { [ordered]@{ name = $_.name; value = $_.value; release = $_.firstReleased } }) }
        })
        $rules = @($index.rules | Where-Object {
            $rule = $_
            ($rule.always -and $selected.Count -gt 0) -or ($rule.whenEnum -and $enumNames.Count) -or
                @($selected | Where-Object { -not $rule.always -and (Test-SettingsContractSelector $rule $_.path ([bool]$_.enumType)) }).Count -gt 0 -or
                @($removed | Where-Object { $_.path -cin $rule.paths }).Count -gt 0
        })
        $result.rules = @($rules | ForEach-Object { [ordered]@{ id = $_.id; text = $_.text } })
        $result.relatedPaths = @($rules.relatedPaths | Where-Object { $_ -and $_ -cnotin $selected.path } | Select-Object -Unique)
    }
    ConvertTo-Json -InputObject $result -Depth 100 -Compress
    exit 0
} catch {
    Write-Error "设置契约查询失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
