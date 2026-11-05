# 将托盘矢量源导出为深浅色多尺寸 ICO；只处理本项目使用的 path、circle 和 rect。
# 从 PowerShell 7 的 STA 进程运行：pwsh -NoProfile -STA -File tools/export-native-tray-icons.ps1。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/AFMediaBar/Assets/Tray'
$culture = [Globalization.CultureInfo]::InvariantCulture
$sizes = @(16, 20, 24, 32, 40, 48, 64)

function Number([string]$value) { [double]::Parse($value, $culture) }

function Render-Frame($svg, [int]$size, $brush) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size / 20.0, $size / 20.0))
        $pen = [Windows.Media.Pen]::new($brush, (Number $svg.'stroke-width'))
        $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
        $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
        $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        foreach ($element in $svg.ChildNodes) {
            switch ($element.LocalName) {
                'path' { $drawing.DrawGeometry($null, $pen, [Windows.Media.Geometry]::Parse($element.d)) }
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

foreach ($name in @('af-native', 'audio-output-native')) {
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
