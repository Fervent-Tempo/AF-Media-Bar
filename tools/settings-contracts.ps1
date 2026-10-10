# 设置契约单入口：查询、生成专题、校验；不启动应用，不访问网络，不修改业务配置。
[CmdletBinding()]
param(
    [ValidateSet('Query', 'Generate', 'Verify')][string]$Action = 'Query',
    [ValidateNotNullOrEmpty()][string[]]$Path,
    [ValidateNotNullOrEmpty()][string]$Prefix,
    [ValidateNotNullOrEmpty()][string[]]$Enum,
    [switch]$List,
    [switch]$Details,
    [ValidateRange(1, 32)][int]$MaxFields = 8,
    [switch]$Check,
    [string]$ContractFile,
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

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

function Format-ContractCell {
    param([AllowNull()][object]$Value)
    (ConvertTo-SettingsContractJson $Value).Replace('|', '\|')
}

function Invoke-SettingsContractQuery {
    param([object]$Catalog, [object]$Index, [string]$Selector, [string[]]$Path, [string]$Prefix,
        [string[]]$Enum, [switch]$Details, [int]$MaxFields)
    $result = [ordered]@{ release = $catalog.latestRelease; schema = $catalog.settingsSchemaVersion; verified = $catalog.verifiedDate; codeBase = $catalog.mainCommit.Substring(0, 7) }
    if ($Selector -eq 'List') {
        $result.groups = @($index.groups | ForEach-Object {
            $group = $_
            [ordered]@{ id = $group.id; title = $group.title; count = @($catalog.fields | Where-Object { Test-SettingsContractSelector $group $_.path }).Count; document = "docs/settings-contracts/$($group.id).md" }
        })
        $result.usage = '-Path 精确JSON路径；-Prefix 前缀最多8项；-Enum 类型名；-Details 增补原始/发布默认与范围。'
    } else {
        $selected = @()
        $removed = @()
        if ($Selector -eq 'Path') {
            foreach ($requested in ($Path | Select-Object -Unique)) {
                $field = @($catalog.fields | Where-Object path -CEQ $requested)
                $old = @($catalog.removedReleasedFields | Where-Object path -CEQ $requested)
                if ($field.Count) { $selected += $field }
                elseif ($old.Count) { $removed += $old }
                else { throw "清单无此路径，不能据此视为未发布：$requested" }
            }
        } elseif ($Selector -eq 'Prefix') {
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
        $enumNames = if ($Selector -eq 'Enum') { @($Enum | Select-Object -Unique) } else { @($selected.enumType | Where-Object { $_ } | Select-Object -Unique) }
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
}

function Export-SettingsContractTopics {
    param([object]$Catalog, [object]$Index, [string]$WorkspaceRoot, [switch]$Check)
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
        $null = $builder.AppendLine('源码位置、原始/发布默认和范围：用 settings-contracts.ps1 -Action Query -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。')
        $content = $builder.ToString().Replace("`r`n", "`n").Replace("`n", "`r`n")
        $path = Join-Path $workspaceRoot "docs/settings-contracts/$($group.id).md"
        if ($Check) {
            if (-not (Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path).Replace("`r`n", "`n") -ne $content.Replace("`r`n", "`n")) {
                throw "专题内容已陈旧：$($group.id)；请重新运行导出工具。"
            }
        } else { [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false)) }
    }
    Write-Host "通过：$($index.groups.Count) 个专题$($(if ($Check) { '与机器清单一致' } else { '已生成' }))。"
}

function Test-SettingsContracts {
    param([object]$Catalog, [object]$Index, [string]$WorkspaceRoot)
    $markdown = Get-Content -LiteralPath (Join-Path $workspaceRoot 'docs/settings-contracts/reference.md') -Raw -Encoding utf8
    $entryPath = Join-Path $workspaceRoot 'docs/settings-contracts.md'
    if ((Get-Item -LiteralPath $entryPath).Length -gt 3072) { throw '默认读取入口超过3KB。' }
    foreach ($field in $catalog.fields) { $null = Get-SettingsContractGroup $Index $field.path }
    $knownPaths = @($catalog.fields.path) + @($catalog.removedReleasedFields.path)
    foreach ($rule in $Index.rules) {
        foreach ($related in $rule.relatedPaths) {
            if ($related -cnotin $knownPaths) { throw "关联规则引用未知路径：$related" }
        }
    }
    $fields = @($catalog.fields)
    $enums = @($catalog.enums)
    $members = @($enums | ForEach-Object { $_.members })
    if (@($fields.path | Select-Object -Unique).Count -ne $fields.Count) { throw '配置路径重复。' }
    if (@($enums.name | Select-Object -Unique).Count -ne $enums.Count) { throw '枚举类型重复。' }
    if ($catalog.counts.paths -ne $fields.Count -or $catalog.counts.enumTypes -ne $enums.Count -or $catalog.counts.enumMembers -ne $members.Count) {
        throw '清单计数与条目不一致。'
    }
    if ($catalog.counts.rootProperties -ne @($fields | Where-Object { $_.path -match '^settings\.[^.]+$' }).Count -or
        $catalog.counts.unreleasedPaths -ne @($fields | Where-Object releaseStatus -EQ 'unreleased').Count -or
        $catalog.counts.unreleasedEnumMembers -ne @($members | Where-Object releaseStatus -EQ 'unreleased').Count) {
        throw '根属性或未发布项计数不一致。'
    }
    $evidenceVersions = @($catalog.releaseEvidence.version)
    if (@($catalog.envelopeFields.path).Count -ne 2 -or
        (Compare-Object (@('schemaVersion', 'settings') | Sort-Object) ($catalog.envelopeFields.path | Sort-Object))) {
        throw '设置 envelope 定义不完整。'
    }
    foreach ($item in @($fields) + @($members)) {
        if ($item.releaseStatus -eq 'released') {
            if (-not $item.firstReleased -or $item.firstReleased -notin $evidenceVersions) { throw '已发布条目缺少已核对版本。' }
        } elseif ($item.releaseStatus -eq 'unreleased') {
            if ($item.firstReleased) { throw '未发布条目不应填写首次发布版本。' }
        } else { throw '条目发布状态不明确。' }
    }
    $fieldRows = [regex]::Matches($markdown, '(?m)^\| `(?<path>settings\.[^`]+)` \| `[^`]+`；(?:是|否) \|')
    $documentedPaths = @($fieldRows | ForEach-Object { $_.Groups['path'].Value })
    if ($documentedPaths.Count -ne $fields.Count -or (Compare-Object ($fields.path | Sort-Object) ($documentedPaths | Sort-Object))) {
        throw 'Markdown 配置路径表与机器清单不一致。'
    }
    foreach ($field in $fields) {
        if ($field.enumType -and $field.enumType -notin $enums.name) { throw "字段缺少枚举定义：$($field.path)" }
        foreach ($boundary in @('onMissing', 'onExplicitNull')) {
            $outcome = $field.$boundary
            if ($null -eq $outcome -or $outcome.accepted -isnot [bool]) { throw "字段缺少读取边界证据：$($field.path)" }
            if ($outcome.accepted -and $outcome.state -notin @('value', 'generated-guid-N', 'entry-removed')) {
                throw "字段读取结果状态不明确：$($field.path)"
            }
        }
    }
    foreach ($enum in $enums) {
        if (@($enum.members.name | Select-Object -Unique).Count -ne @($enum.members).Count) { throw "枚举成员重复：$($enum.name)" }
        if ($enum.invalidInputFallback -notin $enum.members.name) { throw "枚举回退不在定义中：$($enum.name)" }
        $heading = [regex]::Escape("### $($enum.name)")
        $section = [regex]::Match($markdown, "(?ms)^$heading\r?\n(?<body>.*?)(?=^### |\z)")
        if (-not $section.Success) { throw "文档缺少枚举：$($enum.name)" }
        foreach ($member in $enum.members) {
            $status = if ($member.firstReleased) { $member.firstReleased } else { '**未发布**' }
            $row = '| `' + $member.name + '` | ' + $member.value + ' | ' + $status + ' |'
            if (-not $section.Groups['body'].Value.Contains($row)) { throw "枚举文档与清单不一致：$($enum.name).$($member.name)" }
        }
    }
    Export-SettingsContractTopics -Catalog $Catalog -Index $Index -WorkspaceRoot $WorkspaceRoot -Check
    foreach ($source in $catalog.sourceFiles) {
        $sourcePath = [IO.Path]::GetFullPath((Join-Path $workspaceRoot $source.path))
        if (-not $sourcePath.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw '源码路径超出工作区。'
        }
        $content = [IO.File]::ReadAllText($sourcePath).TrimStart([char]0xFEFF).Replace("`r`n", "`n")
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($content))).ToLowerInvariant()
        if ($hash -ne $source.sha256) { throw "源码已变化，请重新核对相关契约：$($source.path)" }
    }
    foreach ($evidence in $catalog.releaseEvidence) {
        $tagCommit = & git -C $workspaceRoot rev-parse "$($evidence.version)^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $evidence.commit) { throw "发布 tag 缺失或提交不符：$($evidence.version)" }
    }
    $latest = $catalog.releaseEvidence | Where-Object version -EQ $catalog.latestRelease
    if (-not $latest -or $latest.commit -ne $catalog.latestReleaseCommit) { throw '最新正式发布证据不一致。' }
    Write-Host "通过：$($fields.Count) 个配置路径、$($enums.Count) 类枚举、$($members.Count) 个成员；文档覆盖、发布 tag 和源码指纹一致。"
    Write-Host '此检查不重新测量默认语义、读取行为或桌面体验；源码变化后须重新核对，不得仅更新指纹。'
}

try {
    $selectorCount = [int]([bool]$Path) + [int]([bool]$Prefix) + [int]([bool]$Enum) + [int]$List.IsPresent
    if ($Action -eq 'Query') {
        if ($selectorCount -gt 1) { throw '查询只接受Path、Prefix、Enum、List中的一种选择。' }
        if ($Check -or $ContractFile) { throw 'Check仅用于Generate，ContractFile仅用于Verify。' }
    } elseif ($selectorCount -or $Details -or $PSBoundParameters.ContainsKey('MaxFields')) {
        throw '生成/校验模式不接受查询参数。'
    }
    if ($Check -and $Action -ne 'Generate') { throw 'Check仅用于Generate。' }
    if ($ContractFile -and $Action -ne 'Verify') { throw 'ContractFile仅用于Verify。' }
    $workspaceRoot = (Resolve-Path -LiteralPath $Workspace).Path
    $data = Read-SettingsContractData $workspaceRoot $ContractFile
    switch ($Action) {
        'Query' {
            $selector = if ($Path) { 'Path' } elseif ($Prefix) { 'Prefix' } elseif ($Enum) { 'Enum' } else { 'List' }
            Invoke-SettingsContractQuery -Catalog $data.Catalog -Index $data.Index -Selector $selector -Path $Path -Prefix $Prefix -Enum $Enum -Details:$Details -MaxFields $MaxFields
        }
        'Generate' { Export-SettingsContractTopics -Catalog $data.Catalog -Index $data.Index -WorkspaceRoot $workspaceRoot -Check:$Check }
        'Verify' { Test-SettingsContracts -Catalog $data.Catalog -Index $data.Index -WorkspaceRoot $workspaceRoot }
    }
    exit 0
} catch {
    Write-Error "设置契约$($Action)失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
