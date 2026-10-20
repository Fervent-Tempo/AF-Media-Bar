# 查询设置发布登记和兼容例外；只检查登记格式，不扫描源码、核验 tag 或启动应用。
[CmdletBinding()]
param(
    [ValidateSet('Query', 'Verify')][string]$Action = 'Query',
    [ValidateNotNullOrEmpty()][string[]]$Path,
    [ValidateNotNullOrEmpty()][string]$Prefix,
    [ValidateNotNullOrEmpty()][string[]]$Enum,
    [switch]$List,
    [ValidateRange(1, 32)][int]$MaxFields = 8,
    [string]$ContractFile,
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'

function Assert-ReleaseMarker {
    param([object]$Value)
    if ($Value -isnot [string] -or $Value -cnotmatch '^(unreleased|v\d+\.\d+\.\d+)$') {
        throw '发布标记只能为 unreleased 或正式版本号 vX.Y.Z。'
    }
}

function Add-SettingsPaths {
    param([System.Collections.IDictionary]$Node, [string]$Parent,
        [System.Collections.IDictionary]$Fields)
    foreach ($key in $Node.Keys) {
        if ($key -ceq '$release') { continue }
        if ($key -ceq '$items') {
            if ($Node[$key] -isnot [System.Collections.IDictionary]) { throw '列表模板必须为对象。' }
            Add-SettingsPaths $Node[$key] ($Parent + '[]') $Fields
            continue
        }
        if ($key -cnotmatch '^[a-z][A-Za-z0-9]*$') { throw "配置键格式无效：$key" }
        $fieldPath = "$Parent.$key"
        $value = $Node[$key]
        if ($value -is [System.Collections.IDictionary]) {
            if (-not $value.Contains('$release')) { throw "对象缺少自己的发布标记：$fieldPath" }
            Assert-ReleaseMarker $value['$release']
            $Fields[$fieldPath] = $value['$release']
            Add-SettingsPaths $value $fieldPath $Fields
        } else {
            Assert-ReleaseMarker $value
            $Fields[$fieldPath] = $value
        }
    }
}

function Read-SettingsRegistry {
    param([string]$File)
    $text = [IO.File]::ReadAllText($File)
    # JSON 重复键会掩盖发布状态，读入前检查；不建立源码指纹或另一份字段快照。
    $document = [System.Text.Json.JsonDocument]::Parse($text)
    try { Test-UniqueJsonKeys $document.RootElement } finally { $document.Dispose() }
    $catalog = ConvertFrom-Json -InputObject $text -AsHashtable
    if ($catalog.formatVersion -ne 2 -or $catalog.settings -isnot [System.Collections.IDictionary] -or
        $catalog.enums -isnot [System.Collections.IDictionary] -or
        $catalog.removed -isnot [System.Collections.IDictionary]) { throw '不支持的设置发布登记格式。' }
    Assert-ReleaseMarker $catalog.latestRelease
    if ($catalog.latestRelease -ceq 'unreleased' -or $catalog.settingsSchemaVersion -isnot [long] -or
        $catalog.settingsSchemaVersion -lt 1) { throw '正式版或设置 schema 登记无效。' }
    $fields = [ordered]@{}
    Add-SettingsPaths $catalog.settings 'settings' $fields
    $enumPaths = @{}
    foreach ($name in $catalog.enums.Keys) {
        $definition = $catalog.enums[$name]
        if ($definition.members -isnot [System.Collections.IDictionary] -or -not $definition.members.Count) {
            throw "枚举缺少成员：$name"
        }
        foreach ($member in $definition.members.Values) {
            if ($member.value -isnot [long]) { throw "枚举数值无效：$name" }
            Assert-ReleaseMarker $member.release
        }
        foreach ($fieldPath in $definition.paths) {
            if (-not $fields.Contains($fieldPath) -or $enumPaths.ContainsKey($fieldPath)) {
                throw "枚举引用未知或重复路径：$fieldPath"
            }
            $enumPaths[$fieldPath] = $name
        }
    }
    foreach ($fieldPath in $catalog.removed.Keys) {
        if ($fields.Contains($fieldPath) -or $fieldPath -cnotmatch '^settings\.') {
            throw "历史删除项与当前字段冲突：$fieldPath"
        }
        $item = $catalog.removed[$fieldPath]
        foreach ($marker in @($item.firstReleased, $item.lastPresentRelease)) {
            Assert-ReleaseMarker $marker
            if ($marker -ceq 'unreleased') { throw '历史删除项必须有正式发布版本。' }
        }
        Assert-ReleaseMarker $item.firstAbsentRelease
        if (-not $item.currentHandling) { throw "历史删除项缺少处理说明：$fieldPath" }
    }
    $ruleIds = @{}
    foreach ($rule in $catalog.rules) {
        if (-not $rule.id -or -not $rule.text -or $ruleIds.ContainsKey($rule.id)) {
            throw '兼容规则缺少内容或 ID 重复。'
        }
        $ruleIds[$rule.id] = $true
        foreach ($fieldPath in @($rule.paths) + @($rule.relatedPaths)) {
            if ($fieldPath -and -not $fields.Contains($fieldPath) -and -not $catalog.removed.Contains($fieldPath)) {
                throw "兼容规则引用未知路径：$fieldPath"
            }
        }
    }
    [pscustomobject]@{ Catalog = $catalog; Fields = $fields; EnumPaths = $enumPaths }
}

function Test-UniqueJsonKeys {
    param([System.Text.Json.JsonElement]$Element)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $keys.Add($property.Name)) { throw "JSON 键重复：$($property.Name)" }
            Test-UniqueJsonKeys $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Test-UniqueJsonKeys $item }
    }
}

function Test-RuleMatch {
    param([System.Collections.IDictionary]$Rule, [string]$FieldPath)
    if ($FieldPath -cin $Rule.paths) { return $true }
    foreach ($prefixValue in $Rule.prefixes) {
        if ($FieldPath.StartsWith($prefixValue, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Get-EnumSummary {
    param([object]$Registry, [string]$Name)
    $definition = $Registry.Catalog.enums[$Name]
    if (-not $definition) { throw "登记无此枚举：$Name" }
    [ordered]@{ name = $Name; members = @($definition.members.Keys | ForEach-Object {
        $member = $definition.members[$_]
        [ordered]@{ name = $_; value = $member.value; release = $member.release }
    }) }
}

try {
    $selectorCount = [int]([bool]$Path) + [int]([bool]$Prefix) + [int]([bool]$Enum) + [int]$List.IsPresent
    if ($selectorCount -gt 1) { throw '查询只接受 Path、Prefix、Enum、List 中的一种选择。' }
    if ($ContractFile -and $Action -ne 'Verify') { throw 'ContractFile 仅用于 Verify。' }
    if ($Action -eq 'Verify' -and ($selectorCount -or $PSBoundParameters.ContainsKey('MaxFields'))) {
        throw 'Verify 不接受查询参数。'
    }
    $file = if ($ContractFile) { $ContractFile } else { Join-Path $Workspace 'docs/settings-contracts.json' }
    $registry = Read-SettingsRegistry $file
    $catalog = $registry.Catalog
    if ($Action -eq 'Verify') {
        Write-Output "通过：$($registry.Fields.Count) 个配置路径、$($catalog.enums.Count) 类枚举的登记格式有效。"
        Write-Output '仅检查 JSON、发布标记和引用；不证明源码覆盖、默认语义、正式发布或桌面体验。'
        exit 0
    }
    $result = [ordered]@{ release = $catalog.latestRelease; schema = $catalog.settingsSchemaVersion }
    if ($List -or $selectorCount -eq 0) {
        $result.sections = @($catalog.settings.Keys | ForEach-Object {
            $name = $_
            $sectionPath = "settings.$name"
            [ordered]@{ path = $sectionPath; count = @($registry.Fields.Keys | Where-Object {
                $_ -ceq $sectionPath -or $_.StartsWith($sectionPath + '.', [StringComparison]::Ordinal)
            }).Count }
        })
        $result.unreleasedPaths = @($registry.Fields.Keys | Where-Object { $registry.Fields[$_] -ceq 'unreleased' })
        $result.usage = '-Path 精确 JSON 路径；-Prefix 前缀；-Enum 枚举；-MaxFields 默认 8，最多 32。'
    } else {
        $selected = @()
        if ($Path) {
            foreach ($requested in ($Path | Select-Object -Unique)) {
                if (-not $registry.Fields.Contains($requested) -and -not $catalog.removed.Contains($requested)) {
                    throw "登记无此路径，不能据此视为未发布：$requested"
                }
                $selected += $requested
            }
        } elseif ($Prefix) {
            $selected = @($registry.Fields.Keys | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) })
            if (-not $selected.Count) { throw "前缀没有匹配路径：$Prefix" }
        }
        if ($selected.Count -gt $MaxFields) { throw "匹配 $($selected.Count) 项，超过 $MaxFields 项；缩小查询范围或提高 MaxFields（最多 32）。" }
        $result.fields = @($selected | Where-Object { $registry.Fields.Contains($_) } | ForEach-Object {
            $release = $registry.Fields[$_]
            [ordered]@{ path = $_; release = $release; status = $(if ($release -ceq 'unreleased') { 'unreleased' } else { 'released' }) }
        })
        $result.removed = @($selected | Where-Object { $catalog.removed.Contains($_) } | ForEach-Object {
            [ordered]@{ path = $_; history = $catalog.removed[$_] }
        })
        $names = if ($Enum) { @($Enum | Select-Object -Unique) } else {
            @($selected | ForEach-Object { $registry.EnumPaths[$_] } | Where-Object { $_ } | Select-Object -Unique)
        }
        if ($names.Count -gt $MaxFields) { throw '枚举查询超过输出上限。' }
        $result.enums = @($names | ForEach-Object { Get-EnumSummary $registry $_ })
        $rules = @($catalog.rules | Where-Object {
            $rule = $_
            ($rule.always -and $selected.Count) -or ($rule.whenEnum -and $names.Count) -or
            @($selected | Where-Object { Test-RuleMatch $rule $_ }).Count
        })
        $result.rules = @($rules | ForEach-Object { [ordered]@{ id = $_.id; text = $_.text } })
        $result.relatedPaths = @($rules.relatedPaths | Where-Object { $_ -and $_ -cnotin $selected } | Select-Object -Unique)
    }
    ConvertTo-Json -InputObject $result -Depth 50 -Compress
    exit 0
} catch {
    Write-Error "设置登记 $Action 失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
