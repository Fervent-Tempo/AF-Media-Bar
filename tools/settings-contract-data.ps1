# 查询、导出和验证共用的只读契约访问；不会将完整机器清单输出到调用方上下文。
function Read-SettingsContractData {
    param([string]$Workspace, [string]$ContractFile)
    $catalogPath = if ($ContractFile) { $ContractFile } else { Join-Path $Workspace 'docs/settings-contracts.json' }
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding utf8 | ConvertFrom-Json
    $index = Get-Content -LiteralPath (Join-Path $Workspace 'docs/settings-contracts/query-index.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if ($catalog.formatVersion -ne 1 -or $index.formatVersion -ne 1) { throw '不支持的设置契约格式。' }
    [pscustomobject]@{ Catalog = $catalog; Index = $index }
}

function Test-SettingsContractSelector {
    param([object]$Selector, [string]$Path, [bool]$HasEnum = $false)
    if ($Selector.always -or ($Selector.whenEnum -and $HasEnum)) { return $true }
    foreach ($prefix in $Selector.excludePrefixes) {
        if ($Path.StartsWith($prefix, [StringComparison]::Ordinal)) { return $false }
    }
    if ($Path -cin $Selector.paths) { return $true }
    foreach ($prefix in $Selector.prefixes) {
        if ($Path.StartsWith($prefix, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Get-SettingsContractGroup {
    param([object]$Index, [string]$Path)
    $groups = @($Index.groups | Where-Object { Test-SettingsContractSelector $_ $Path })
    if ($groups.Count -ne 1) { throw "配置路径需映射到唯一专题：$Path" }
    $groups[0]
}

function ConvertTo-SettingsContractJson {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return 'null' }
    ConvertTo-Json -InputObject $Value -Depth 100 -Compress
}

function ConvertTo-SettingsContractOutcome {
    param([object]$Outcome)
    if (-not $Outcome.accepted) { return "读取失败：$($Outcome.error)" }
    switch ($Outcome.state) {
        'generated-guid-N' { return '生成N格式GUID' }
        'entry-removed' { return '条目被过滤' }
        default { return ,$Outcome.value }
    }
}
