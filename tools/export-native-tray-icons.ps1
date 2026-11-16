# 从快捷控制字形生成对应矢量源，再导出深浅色多尺寸 ICO；支持 path、circle 和 rect。
# 从 PowerShell 7 的 STA 进程运行：pwsh -NoProfile -STA -File tools/export-native-tray-icons.ps1。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/AFMediaBar/Assets/Tray'
$culture = [Globalization.CultureInfo]::InvariantCulture
$sizes = @(16, 20, 24, 32, 40, 48, 64)

function Number([string]$value) { [double]::Parse($value, $culture) }

# 字形与字体名称均读取程序的权威定义，避免托盘和快捷控制各维护一份。
$projectDirectory = Split-Path $assetDirectory -Parent | Split-Path -Parent
$glyphSource = Get-Content -LiteralPath (Join-Path $projectDirectory 'Components/Common/MediaControlGlyphs.cs') -Raw
[xml]$appMarkup = Get-Content -LiteralPath (Join-Path $projectDirectory 'App.xaml') -Raw
$fontNode = $appMarkup.GetElementsByTagName('FontFamily') | Where-Object {
    $_.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml') -eq 'AppIconFontFamily'
} | Select-Object -First 1
if ($null -eq $fontNode) { throw '找不到 AppIconFontFamily。' }
$glyphs = [ordered]@{
    'output-device-native' = 'OutputDevice'
    'input-device-native' = 'InputDevice'
    'volume-native' = 'Volume'
}
foreach ($entry in $glyphs.GetEnumerator()) {
    $match = [regex]::Match($glyphSource, 'public const string ' + $entry.Value + '\s*=\s*"\\u([0-9a-fA-F]{4})"')
    if (!$match.Success) { throw "找不到快捷控制字形：$($entry.Value)" }
    $text = [Windows.Media.FormattedText]::new([char]::ConvertFromUtf32([Convert]::ToInt32($match.Groups[1].Value, 16)),
        $culture, [Windows.FlowDirection]::LeftToRight, [Windows.Media.Typeface]::new($fontNode.InnerText),
        20, [Windows.Media.Brushes]::Black, 1.0)
    $geometry = $text.BuildGeometry([Windows.Point]::new(0, 0))
    $bounds = $geometry.Bounds
    $scale = 16.0 / [Math]::Max($bounds.Width, $bounds.Height)
    $transform = [Windows.Media.TransformGroup]::new()
    $transform.Children.Add([Windows.Media.ScaleTransform]::new($scale, $scale))
    $transform.Children.Add([Windows.Media.TranslateTransform]::new(
        (20 - $bounds.Width * $scale) / 2 - $bounds.X * $scale,
        (20 - $bounds.Height * $scale) / 2 - $bounds.Y * $scale))
    $geometry.Transform = $transform
    $path = $geometry.GetFlattenedPathGeometry(0.01, [Windows.Media.ToleranceType]::Absolute)
    $data = $path.ToString($culture) -replace '^F[01]\s*', ''
    $rule = $path.FillRule.ToString().ToLowerInvariant()
    $svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" fill-rule="' + $rule + '"><path d="' + $data + '"/></svg>'
    [IO.File]::WriteAllText((Join-Path $assetDirectory "$($entry.Key).svg"), $svg)
}

function Render-Frame($svg, [int]$size, $brush) {
    $artwork = [Windows.Media.DrawingGroup]::new()
    $drawing = $artwork.Open()
    try {
        $fill = if ($svg.fill -eq 'currentColor') { $brush } else { $null }
        $pen = $null
        if ($svg.HasAttribute('stroke-width')) {
            $pen = [Windows.Media.Pen]::new($brush, (Number $svg.'stroke-width'))
            $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
            $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
            $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        }
        foreach ($element in $svg.ChildNodes) {
            switch ($element.LocalName) {
                'path' {
                    $prefix = if ($svg.'fill-rule' -eq 'nonzero') { 'F1 ' } else { 'F0 ' }
                    $drawing.DrawGeometry($fill, $pen, [Windows.Media.Geometry]::Parse($prefix + $element.d))
                }
                'circle' {
                    $drawing.DrawEllipse($null, $pen, [Windows.Point]::new((Number $element.cx), (Number $element.cy)),
                        (Number $element.r), (Number $element.r))
                }
                'rect' {
                    $radius = if ($element.HasAttribute('rx')) { Number $element.rx } else { 0 }
                    $drawing.DrawRoundedRectangle($null, $pen,
                        [Windows.Rect]::new((Number $element.x), (Number $element.y), (Number $element.width), (Number $element.height)),
                        $radius, $radius)
                }
                default { throw "不支持的矢量元素：$($element.LocalName)" }
            }
        }
    } finally { $drawing.Close() }

    # 按含描边的可见边界居中，长边占 ICO 画布的 90%。
    $bounds = $artwork.Bounds
    $scale = $size * 0.9 / [Math]::Max($bounds.Width, $bounds.Height)
    $offsetX = ($size - $bounds.Width * $scale) / 2 - $bounds.X * $scale
    $offsetY = ($size - $bounds.Height * $scale) / 2 - $bounds.Y * $scale
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $matrix = [Windows.Media.Matrix]::new($scale, 0, 0, $scale, $offsetX, $offsetY)
        $drawing.PushTransform([Windows.Media.MatrixTransform]::new($matrix))
        $drawing.DrawDrawing($artwork)
        $drawing.Pop()
    } finally { $drawing.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

foreach ($name in @('af-native', 'audio-output-native') + @($glyphs.Keys)) {
    [xml]$document = Get-Content -LiteralPath (Join-Path $assetDirectory "$name.svg") -Raw
    foreach ($color in @('dark', 'white')) {
        $brush = if ($color -eq 'white') { [Windows.Media.Brushes]::White }
            else { [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0x1C, 0x1C, 0x1C)) }
        $frames = [Collections.Generic.List[byte[]]]::new()
        foreach ($size in $sizes) { $frames.Add((Render-Frame $document.DocumentElement $size $brush)) }
        $stream = [IO.File]::Create((Join-Path $assetDirectory "${name}_${color}.ico"))
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$sizes.Count)
            $offset = 6 + 16 * $sizes.Count
            for ($index = 0; $index -lt $sizes.Count; $index++) {
                $writer.Write([byte]$sizes[$index])
                $writer.Write([byte]$sizes[$index])
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$frames[$index].Length)
                $writer.Write([uint32]$offset)
                $offset += $frames[$index].Length
            }
            foreach ($frame in $frames) { $writer.Write($frame) }
        } finally { $writer.Dispose() }
    }
}
