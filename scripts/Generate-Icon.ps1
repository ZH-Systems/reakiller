$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$sizes = @(16,24,32,48,64,128,256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($n in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($n,$n)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = $n / 256.0
    $g.ScaleTransform([single]$scale,[single]$scale)
    $blue = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0,91,189))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $aqua = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(17,213,241),19)
    $font = [System.Drawing.Font]::new('Arial',174,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
    $g.FillRectangle($blue,8,8,240,240)
    $g.DrawString('R',$font,$white,27,11)
    $aqua.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $aqua.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
    $g.DrawArc($aqua,57,97,130,110,170,125)
    $g.FillRectangle($blue,155,156,77,68)
    $frame = [System.Drawing.Pen]::new([System.Drawing.Color]::White,9)
    $g.DrawRectangle($frame,155,156,77,68)
    $g.FillEllipse($white,206,167,14,14)
    $points = [System.Drawing.Point[]]@(
      [System.Drawing.Point]::new(162,214),
      [System.Drawing.Point]::new(184,187),
      [System.Drawing.Point]::new(199,203),
      [System.Drawing.Point]::new(211,192),
      [System.Drawing.Point]::new(225,214))
    $g.FillPolygon($white,$points)
    $stream = [System.IO.MemoryStream]::new()
    $bmp.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    if ($n -eq 256) { $bmp.Save((Join-Path $dir 'ReaKiller.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $g.Dispose(); $bmp.Dispose()
    $blue.Dispose(); $white.Dispose(); $aqua.Dispose(); $font.Dispose(); $frame.Dispose()
}
$path = Join-Path $dir 'ReaKiller.ico'
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
Write-Host "Generated $path with $($sizes.Count) image sizes"
