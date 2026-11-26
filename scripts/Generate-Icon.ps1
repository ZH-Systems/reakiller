$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
# Transflux emerald layered identity.
$sizes = @(16,24,32,48,64,128,256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($n in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($n,$n)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform([single]($n / 64.0),[single]($n / 64.0))
    $navy = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#142C25'))
    $background = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $background.AddArc(0,0,26,26,180,90)
    $background.AddArc(38,0,26,26,270,90)
    $background.AddArc(38,38,26,26,0,90)
    $background.AddArc(0,38,26,26,90,90)
    $background.CloseFigure()
    $g.FillPath($navy,$background)
    $layers = @(
        @{ Color = '#448F71'; Y = 38 },
        @{ Color = '#85CBAA'; Y = 30.5 },
        @{ Color = '#DBF4E8'; Y = 22.5 }
    )
    foreach ($layer in $layers) {
        $y = [single]$layer.Y
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($layer.Color))
        $outline = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#142C25'),1.5)
        $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(12,$y),
            [System.Drawing.PointF]::new(31,($y-10.5)),
            [System.Drawing.PointF]::new(52,$y),
            [System.Drawing.PointF]::new(33,($y+11))
        )
        $g.FillPolygon($brush,$points)
        $g.DrawPolygon($outline,$points)
        $brush.Dispose(); $outline.Dispose()
    }
    $stream = [System.IO.MemoryStream]::new()
    $bmp.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    if ($n -eq 256) { $bmp.Save((Join-Path $dir 'Transflux.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $g.Dispose(); $bmp.Dispose()
    $navy.Dispose(); $background.Dispose()
}
$path = Join-Path $dir 'Transflux.ico'
$fs = [System.IO.File]::Create($path)
$writer = [System.IO.BinaryWriter]::new($fs)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $n = $sizes[$i]; $payload = $images[$i]
        $writer.Write([byte]($n % 256)); $writer.Write([byte]($n % 256))
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$payload.Length); $writer.Write([uint32]$offset)
        $offset += $payload.Length
    }
    foreach ($payload in $images) { $writer.Write($payload) }
} finally { $writer.Dispose() }
Write-Host "Generated Transflux icon in $path"
