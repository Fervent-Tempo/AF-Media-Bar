# 供清单与亮点目录生成共用的文案解析；只返回纯文本，不读取安装包或写入文件。
function Get-ValidatedReleaseHighlightsContent([object]$Content) {
    if ($Content -isnot [System.Collections.IDictionary] -or
        $Content.sections -isnot [System.Collections.IList] -or $Content.sections.Count -eq 0) {
        throw 'Invalid structured release sections.'
    }
    $summary = ''
    if ($Content.Contains('summary')) {
        if ($Content.summary -isnot [string]) { throw 'Invalid release summary.' }
        $summary = $Content.summary.Trim()
    }
    $sections = @($Content.sections | ForEach-Object {
        if ($_ -isnot [System.Collections.IDictionary] -or $_.title -isnot [string] -or
            [string]::IsNullOrWhiteSpace($_.title) -or $_.items -isnot [System.Collections.IList] -or $_.items.Count -eq 0) {
            throw 'Invalid release section.'
        }
        $section = $_
        $items = @($section.items | ForEach-Object {
            if ($_ -isnot [System.Collections.IDictionary] -or $_.title -isnot [string] -or
                [string]::IsNullOrWhiteSpace($_.title) -or $_.description -isnot [string] -or
                [string]::IsNullOrWhiteSpace($_.description)) { throw 'Invalid release item.' }
            [ordered]@{ title = $_.title.Trim(); description = $_.description.Trim() }
        })
        [ordered]@{ title = $section.title.Trim(); items = $items }
    })
    return [ordered]@{ summary = $summary; sections = $sections }
}

function Join-ReleaseNotesParagraphs([string[]]$Lines) {
    $paragraphs = [Collections.Generic.List[string]]::new()
    $words = [Collections.Generic.List[string]]::new()
    foreach ($line in @($Lines) + @('')) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            if ($words.Count -gt 0) { $paragraphs.Add(($words -join ' ')); $words.Clear() }
        } else { $words.Add($line.Trim()) }
    }
    return $paragraphs -join "`n`n"
}

function Get-ReleaseNotesText([string]$Path, [string]$Version) {
    $lines = @(Get-Content -LiteralPath $Path -Encoding utf8)
    if ($lines.Count -eq 0 -or $lines[0] -notmatch '^# (?<title>AF Media Bar (?<version>\d+\.\d+\.\d+(?:\.\d+)?)(?:\s.*)?)$') {
        throw 'Release notes must start with the matching AF Media Bar version heading.'
    }
    $title = $Matches.title
    $notesVersion = $Matches.version
    if ([version]($notesVersion + $(if ($notesVersion.Split('.').Count -eq 3) { '.0' } else { '' })) -ne
        [version]($Version + $(if ($Version.Split('.').Count -eq 3) { '.0' } else { '' }))) {
        throw 'Release notes version differs from the manifest.'
    }
    $body = [Collections.Generic.List[string]]::new()
    foreach ($line in ($lines | Select-Object -Skip 1)) {
        if ($line -match '^## (下载|已知|贡献|首次|Downloads|Known|Contributors|New Contributors)') { break }
        $body.Add($line)
    }
    $highlights = [Collections.Generic.List[string]]::new()
    $content = $null
    if (($body -join "`n") -match '<\s*/?(details|summary)\b') {
        $sections = [Collections.Generic.List[object]]::new()
        $intro = [Collections.Generic.List[string]]::new()
        $description = [Collections.Generic.List[string]]::new()
        $section = $null
        $itemTitle = $null
        $isOpen = $false
        foreach ($line in $body) {
            $value = $line.Trim()
            if ($value -eq '<details>') {
                if ($isOpen -or $null -eq $section) { throw 'Nested or ungrouped release item.' }
                $isOpen = $true
                $itemTitle = $null
                $description.Clear()
            } elseif ($value -match '^<summary>(?<text>[^<>]+)</summary>$') {
                if (-not $isOpen -or $null -ne $itemTitle -or $description.Count -gt 0) { throw 'Misplaced release summary.' }
                $itemTitle = $Matches.text.Trim()
            } elseif ($value -eq '</details>') {
                $text = Join-ReleaseNotesParagraphs $description.ToArray()
                if (-not $isOpen -or [string]::IsNullOrWhiteSpace($itemTitle) -or [string]::IsNullOrWhiteSpace($text)) {
                    throw 'Release item needs a title and description.'
                }
                $section.items.Add([ordered]@{ title = $itemTitle; description = $text })
                $isOpen = $false
            } elseif ($value -match '^## (?<text>.+)$') {
                if ($isOpen) { throw 'Unclosed release item.' }
                $section = [ordered]@{ title = $Matches.text.Trim(); items = [Collections.Generic.List[object]]::new() }
                $sections.Add($section)
            } elseif ($value -match '<[^>]+>|^#{1,6}\s|^[-*]\s') {
                throw 'Release items support plain paragraphs inside details only.'
            } elseif ($isOpen) {
                if ($null -eq $itemTitle) {
                    if ($value) { throw 'Release item must start with summary.' }
                } else { $description.Add($value) }
            } elseif ($null -eq $section) { $intro.Add($value) }
            elseif ($value) { throw 'Release section text must be inside details.' }
        }
        if ($isOpen) { throw 'Unclosed release item.' }
        $content = Get-ValidatedReleaseHighlightsContent ([ordered]@{
            summary = (Join-ReleaseNotesParagraphs $intro.ToArray()); sections = $sections.ToArray()
        })
        if ($content.summary) { $highlights.Add($content.summary) }
        foreach ($group in $content.sections) {
            foreach ($item in $group.items) { $highlights.Add($group.title + ' — ' + $item.title + '：' + $item.description) }
        }
    } else {
        foreach ($line in $body) {
            $value = ($line -replace '^\s*[-*]\s+', '').Trim()
            if ($value -match '<[^>]+>') { throw 'Release highlights must not contain HTML.' }
            if ($value -and $value -notmatch '^#') { $highlights.Add($value) }
        }
    }
    if ($highlights.Count -eq 0 -or $highlights.Count -gt 20) {
        throw 'Release notes must contain 1–20 complete compatibility entries; shorten the notes rather than truncate them.'
    }
    return [ordered]@{ title = $title; highlights = $highlights.ToArray(); content = $content }
}
