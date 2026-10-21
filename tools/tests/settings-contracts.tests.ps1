# 验证发布登记查询不混同字段/枚举/历史状态，并拒绝格式错误；不构建或启动应用。
[CmdletBinding()]
param([string]$Workspace = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference = 'Stop'
$tool = Join-Path $Workspace 'tools/settings-contracts.ps1'
$registryPath = Join-Path $Workspace 'docs/settings-contracts.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("settings-registry-$([Guid]::NewGuid().ToString('N'))")
$fixture = Join-Path $fixtureRoot 'docs/settings-contracts.json'

function Assert-Registry {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}
function Invoke-RegistryTool {
    param([string[]]$Arguments, [string]$Failure, [string]$Action = 'Query')
    $output = & pwsh -NoProfile -File $tool -Action $Action -Workspace $fixtureRoot @Arguments 2>&1
    $code = $LASTEXITCODE
    $text = $output -join "`n"
    if ($Failure) {
        Assert-Registry ($code -eq 1 -and $text -match $Failure) "未按预期拒绝：$text"
        return $null
    }
    Assert-Registry ($code -eq 0) "查询失败：$text"
    [pscustomobject]@{ Text = $text; Data = $(if ($Action -eq 'Query') { $text | ConvertFrom-Json });
        Bytes = [Text.Encoding]::UTF8.GetByteCount($text) }
}
function Write-Fixture {
    param([object]$Value)
    [IO.File]::WriteAllText($fixture, ($Value | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
}

try {
    $before = (Get-Item -LiteralPath $registryPath).LastWriteTimeUtc.Ticks
    # 查询语义使用独立夹具，字段的真实发布状态由登记文件维护。
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $fixture))
    $validRegistry = @'
{
  "formatVersion": 2,
  "latestRelease": "v1.2.0",
  "settingsSchemaVersion": 2,
  "settings": {
    "position": "v1.2.0",
    "appearance": { "$release": "v1.2.0", "fontWeight": "v1.2.0", "idleNoteColor": "unreleased" },
    "interaction": { "$release": "v1.2.0", "artworkHoverMode": "unreleased" },
    "interfaceLanguage": "v1.2.0",
    "quickLaunch": { "$release": "v1.2.0", "entries": { "$release": "v1.2.0", "$items": { "target": "v1.2.0" } } },
    "taskbarExperience": { "$release": "v1.2.0", "hoverControls": {
      "$release": "v1.2.0", "playPauseVisible": "v1.2.0", "previousNextVisible": "v1.2.0",
      "progressVisible": "v1.2.0", "outputDeviceVisible": "v1.2.0", "volumeVisible": "v1.2.0"
    } }
  },
  "enums": {
    "InterfaceLanguage": { "paths": ["settings.interfaceLanguage"], "members": {
      "System": { "value": 0, "release": "v1.2.0" }, "Vietnamese": { "value": 4, "release": "unreleased" }
    } },
    "ArtworkHoverMode": { "paths": ["settings.interaction.artworkHoverMode"], "members": {
      "Off": { "value": 0, "release": "unreleased" }, "Zoom": { "value": 1, "release": "unreleased" },
      "Preview": { "value": 2, "release": "unreleased" }
    } }
  },
  "removed": { "settings.lyricsMatchStrictness": {
    "firstReleased": "v1.2.0", "lastPresentRelease": "v1.2.0", "firstAbsentRelease": "unreleased", "currentHandling": "ignored"
  } },
  "rules": [
    { "id": "artwork-hover", "paths": ["settings.interaction.artworkHoverMode"], "text": "Fixture rule" },
    { "id": "removed-strictness", "paths": ["settings.lyricsMatchStrictness"], "text": "Fixture history" }
  ]
}
'@ | ConvertFrom-Json -AsHashtable
    Write-Fixture $validRegistry
    $cover = Invoke-RegistryTool -Arguments @('-Path', 'settings.interaction.artworkHoverMode')
    Assert-Registry (@($cover.Data.fields).Count -eq 1 -and $cover.Data.fields[0].release -eq 'unreleased') '封面模式的开发状态丢失。'
    Assert-Registry (@($cover.Data.enums[0].members).Count -eq 3 -and 'artwork-hover' -in $cover.Data.rules.id) '封面枚举或关联规则丢失。'
    Assert-Registry ($cover.Bytes -le 3072 -and 'default' -notin $cover.Data.fields[0].PSObject.Properties.Name -and
        'source' -notin $cover.Data.fields[0].PSObject.Properties.Name) '查询重新带入默认快照或源码位置。'
    $language = Invoke-RegistryTool -Arguments @('-Path', 'settings.interfaceLanguage')
    $vietnamese = $language.Data.enums[0].members | Where-Object name -EQ 'Vietnamese'
    Assert-Registry ($language.Data.fields[0].status -eq 'released' -and $vietnamese.release -eq 'unreleased') '字段与新枚举成员发布状态被混同。'
    $entry = Invoke-RegistryTool -Arguments @('-Path', 'settings.quickLaunch.entries[].target')
    Assert-Registry ($entry.Data.fields[0].release -eq 'v1.2.0') '列表条目路径无法查询。'
    $old = Invoke-RegistryTool -Arguments @('-Path', 'settings.lyricsMatchStrictness')
    Assert-Registry (@($old.Data.fields).Count -eq 0 -and $old.Data.removed[0].history.firstReleased -eq 'v1.2.0' -and
        'removed-strictness' -in $old.Data.rules.id) '已删除的发布项被误当成开发项。'
    $enums = Invoke-RegistryTool -Arguments @('-Enum', 'ArtworkHoverMode')
    Assert-Registry (@($enums.Data.fields).Count -eq 0 -and @($enums.Data.enums).Count -eq 1) '纯枚举查询混入字段。'
    $prefix = Invoke-RegistryTool -Arguments @('-Prefix', 'settings.taskbarExperience.hoverControls')
    Assert-Registry (@($prefix.Data.fields).Count -eq 6) '指定前缀遗漏当前成员。'
    $list = Invoke-RegistryTool -Arguments @('-List')
    Assert-Registry ('settings.appearance.idleNoteColor' -in $list.Data.unreleasedPaths -and @($list.Data.sections).Count -gt 0) '开发项目录缺失。'
    Invoke-RegistryTool -Arguments @('-Prefix', 'settings') -Failure '超过 8 项' | Out-Null
    Invoke-RegistryTool -Arguments @('-Path', 'settings.notRegistered') -Failure '不能据此视为未发布' | Out-Null
    Invoke-RegistryTool -Arguments @('-Enum', 'NotRegistered') -Failure '登记无此枚举' | Out-Null
    Invoke-RegistryTool -Arguments @('-Path', 'settings.position', '-Enum', 'ArtworkHoverMode') -Failure '一种选择' | Out-Null
    Invoke-RegistryTool -Action Verify -Arguments @('-Path', 'settings.position') -Failure '不接受查询参数' | Out-Null
    $verify = Invoke-RegistryTool -Action Verify
    Assert-Registry ($verify.Text -match '不证明源码覆盖') '格式检查夸大验证范围。'
    & pwsh -NoProfile -File $tool -Action Verify -Workspace $Workspace
    Assert-Registry ($LASTEXITCODE -eq 0) '当前仓库登记格式无效。'
    Assert-Registry ((Get-Item -LiteralPath $registryPath).LastWriteTimeUtc.Ticks -eq $before) '查询或格式检查改写了登记文件。'
    $data = $validRegistry | ConvertTo-Json -Depth 50 | ConvertFrom-Json -AsHashtable
    $data.settings.appearance.fontWeight = 'released'
    Write-Fixture $data
    Invoke-RegistryTool -Action Verify -Arguments @('-ContractFile', $fixture) -Failure '发布标记只能' | Out-Null
    $data.settings.appearance.fontWeight = 'v1.2.0'
    $data.enums.InterfaceLanguage.paths = @('settings.notRegistered')
    Write-Fixture $data
    Invoke-RegistryTool -Action Verify -Arguments @('-ContractFile', $fixture) -Failure '引用未知或重复路径' | Out-Null
    [IO.File]::WriteAllText($fixture, '{"formatVersion":2,"formatVersion":2}')
    Invoke-RegistryTool -Action Verify -Arguments @('-ContractFile', $fixture) -Failure 'JSON 键重复' | Out-Null
    $data = $validRegistry | ConvertTo-Json -Depth 50 | ConvertFrom-Json -AsHashtable
    $data.latestRelease = 'v9.9.9'
    Write-Fixture $data
    # 形式合法的版本无需在本地存在 tag；通过不能作为正式发布证明。
    Invoke-RegistryTool -Action Verify -Arguments @('-ContractFile', $fixture) | Out-Null
    Write-Output '通过：字段/枚举/历史查询、列表模板、输出范围、错误登记拒绝及只读格式检查。'
    exit 0
} catch {
    Write-Error "设置登记测试失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if (-not $resolvedFixtureRoot.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw '测试夹具不在临时目录中。'
        }
        Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse
    }
}
