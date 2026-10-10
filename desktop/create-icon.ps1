$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot 'AccountManagement/Assets'
New-Item -ItemType Directory -Force $assetDirectory | Out-Null

function RoundedPath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

# Vector geometry drawn at 4x resolution; transparent outside the rounded tile.
$master = [Drawing.Bitmap]::new(1024, 1024)
$graphics = [Drawing.Graphics]::FromImage($master)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.ScaleTransform(4, 4)
$background = [Drawing.Drawing2D.LinearGradientBrush]::new(
    [Drawing.Point]::new(20, 20), [Drawing.Point]::new(236, 236),
    [Drawing.ColorTranslator]::FromHtml('#173C69'), [Drawing.ColorTranslator]::FromHtml('#0078D4'))
$blue = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#0067C0'))
$muted = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#639BD1'))
$shackle = [Drawing.Pen]::new($blue, 12)
$tile = RoundedPath 8 8 240 240 50
$card = RoundedPath 40 62 176 132 20
$lock = RoundedPath 137 124 59 51 10
try {
    $graphics.FillPath($background, $tile)
    $graphics.FillPath([Drawing.Brushes]::White, $card)
    $graphics.FillEllipse($muted, 64, 86, 35, 35)
    $graphics.FillPie($muted, 57, 128, 49, 51, 180, 180)
    $graphics.DrawArc($shackle, 147, 98, 39, 40, 180, 180)
    $graphics.DrawLine($shackle, 147, 118, 147, 130)
    $graphics.DrawLine($shackle, 186, 118, 186, 130)
    $graphics.FillPath($blue, $lock)
    $graphics.FillEllipse([Drawing.Brushes]::White, 162, 138, 10, 10)
    $graphics.FillRectangle([Drawing.Brushes]::White, 164, 144, 6, 15)
} finally {
    $graphics.Dispose(); $background.Dispose(); $blue.Dispose(); $muted.Dispose()
    $shackle.Dispose(); $tile.Dispose(); $card.Dispose(); $lock.Dispose()
}

$frames = @()
try {
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $drawing = [Drawing.Graphics]::FromImage($bitmap)
        $drawing.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $drawing.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $drawing.DrawImage($master, 0, 0, $size, $size)
        $stream = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += ,@($size, $stream.ToArray())
            if ($size -eq 256) { $bitmap.Save((Join-Path $assetDirectory 'app-icon.png'), [Drawing.Imaging.ImageFormat]::Png) }
        } finally { $stream.Dispose(); $drawing.Dispose(); $bitmap.Dispose() }
    }
} finally { $master.Dispose() }
$file = [IO.File]::Create((Join-Path $assetDirectory 'app.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $sizeByte = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
        $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
        $offset += $frame[1].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
} finally { $writer.Dispose() }
Write-Output 'Generated Assets/app.ico (9 sizes) and app-icon.png'
