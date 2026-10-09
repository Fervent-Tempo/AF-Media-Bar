# 验证设置清单的结构、文档覆盖及已核对源码证据；只读，不运行应用或访问网络。
[CmdletBinding()]
param(
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot),
    [string]$ContractFile
)

$ErrorActionPreference = 'Stop'
try {
    $workspaceRoot = (Resolve-Path -LiteralPath $Workspace).Path
    $catalogPath = if ($ContractFile) { (Resolve-Path -LiteralPath $ContractFile).Path } else { Join-Path $workspaceRoot 'docs/settings-contracts.json' }
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($catalog.formatVersion -ne 1) { throw '不支持的契约清单格式。' }
    $markdown = Get-Content -LiteralPath (Join-Path $workspaceRoot 'docs/settings-contracts.md') -Raw -Encoding utf8
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
    exit 0
} catch {
    Write-Error "设置契约检查失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
