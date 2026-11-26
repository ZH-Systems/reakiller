$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
# Transflux identity: opposed arrows on navy, with a white central transfer line.
$sizes = @(16,24,32,48,64,128,256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($n in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($n,$n)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform([single]($n / 64.0),[single]($n / 64.0))
    $navy = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#14243c'))
    $blue = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#60a5fa'),5)
    $white = [System.Drawing.Pen]::new([System.Drawing.Color]::White,4)
    $blue.StartCap = $blue.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $white.StartCap = $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $blue.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $background = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $background.AddArc(0,0,26,26,180,90)
    $background.AddArc(38,0,26,26,270,90)
    $background.AddArc(38,38,26,26,0,90)
    $background.AddArc(0,38,26,26,90,90)
    $background.CloseFigure()
    $g.FillPath($navy,$background)
    $g.DrawLines($blue,[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(13,21),[System.Drawing.PointF]::new(46,21),[System.Drawing.PointF]::new(37,12)))
    $g.DrawLines($blue,[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(46,43),[System.Drawing.PointF]::new(13,43),[System.Drawing.PointF]::new(22,52)))
    $g.DrawLine($white,20,32,44,32)
    $stream = [System.IO.MemoryStream]::new()
    $bmp.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    if ($n -eq 256) { $bmp.Save((Join-Path $dir 'ReaKiller.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $g.Dispose(); $bmp.Dispose()
    $navy.Dispose(); $blue.Dispose(); $white.Dispose(); $background.Dispose()
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
Write-Host "Generated Transflux icon in $path"
