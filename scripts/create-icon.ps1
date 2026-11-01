# Render the supplied full-color kit's vector lettermark into transparent Windows icon frames.
. "$PSScriptRoot/env.ps1"
Add-Type -AssemblyName PresentationCore, WindowsBase
$iconPath = Join-Path $repoRoot 'src/FullTilt/Assets/FullTilt.ico'
$logo = Get-Content -LiteralPath (Join-Path $repoRoot 'src/FullTilt/Assets/FullTilt-logo.json') -Raw | ConvertFrom-Json
$geometry = [System.Windows.Media.Geometry]::Parse($logo.Lettermark)
$geometry.Freeze()
$brush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString($logo.Lime))
$brush.Freeze()
$bounds = $geometry.Bounds
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $scale = $size * .84 / [Math]::Max($bounds.Width, $bounds.Height)
    $offsetX = ($size - $bounds.Width * $scale) / 2 - $bounds.X * $scale
    $offsetY = ($size - $bounds.Height * $scale) / 2 - $bounds.Y * $scale
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.MatrixTransform]::new([System.Windows.Media.Matrix]::new($scale, 0, 0, $scale, $offsetX, $offsetY)))
    $drawing.DrawGeometry($brush, $null, $geometry)
    $drawing.Pop(); $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new()
    $encoder.Save($memory)
    $frames += ,@($size, $memory.ToArray())
    $memory.Dispose()
}
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
    $offset += $frame[1].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
$writer.Dispose(); $file.Dispose()
Write-Host "Created full-color kit icon: $iconPath"