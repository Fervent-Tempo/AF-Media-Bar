param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\src\AFMediaBar')
)

$ErrorActionPreference = 'Stop'
$viewModelRoot = Join-Path $SourceRoot 'ViewModels'
$violations = @()

function Test-XmlDocumentation {
    param(
        [string[]]$Lines,
        [int]$DeclarationIndex
    )

    for ($index = $DeclarationIndex - 1; $index -ge 0; $index--) {
        $trimmed = $Lines[$index].Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('[')) {
            continue
        }

        return $trimmed.StartsWith('///')
    }

    return $false
}

foreach ($file in Get-ChildItem -LiteralPath $viewModelRoot -Filter '*.cs' -Recurse) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($pattern in @('Wpf\.Ui\.Controls', 'AFMediaBar\.Views', 'App\.Services', '\bSettingsWindow\b', '\bNavigationViewItem\b', '\bSymbolIcon\b')) {
        if ($text -match $pattern) {
            $violations += "ViewModel UI dependency: $($file.FullName) matches $pattern"
        }
    }

    if ($text -match '(?m)\bnew\s+[A-Za-z_][A-Za-z0-9_]*Service\b') {
        $violations += "ViewModel manual service construction: $($file.FullName)"
    }
}

$publicTypeCount = 0
foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Filter '*.cs' -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $lines = Get-Content -LiteralPath $file.FullName
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\s*public\s+(?:(?:abstract|sealed|static|partial|unsafe|readonly|new)\s+)*(?:class|record|struct|interface|enum|delegate)\s+\w+') {
            $publicTypeCount++
            if (-not (Test-XmlDocumentation -Lines $lines -DeclarationIndex $index)) {
                $violations += "Public type lacks bilingual XML documentation: $($file.FullName):$($index + 1)"
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'ViewModel boundary scan passed.'
Write-Output "Public type XML documentation scan passed ($publicTypeCount types)."
