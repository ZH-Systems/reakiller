$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
# Selected ReaKiller artwork: teal #14b1ba, white R and a single right-pointing arrow.
# The outlines are traced from the approved turquoise_r_refresh_icon.png artwork.
$outer = '288,220 281,229 282,1030 287,1035 468,1033 471,767 590,766 783,1034 997,1034 999,1027 799,738 862,712 903,682 940,637 968,572 979,506 976,432 956,364 922,309 886,274 845,248 800,230 752,220'
$cutout = '797,416 820,462 825,513 808,570 777,606 748,623 712,633 561,635 495,647 422,682 356,739 411,658 450,622 495,592 578,559 670,546 671,609 789,511 671,413 670,478 602,492 530,521 471,558 416,608 371,671 350,717 336,770 304,826 308,737 340,651 387,584 425,547 471,514 472,366 712,368 763,386'
function Polygon([string]$coordinates) {
    $points = @($coordinates.Split(' ') | ForEach-Object {
        $pair = $_.Split(',')
        [System.Drawing.PointF]::new([single]::Parse($pair[0], [System.Globalization.CultureInfo]::InvariantCulture),[single]::Parse($pair[1],[System.Globalization.CultureInfo]::InvariantCulture))
    })
    return ,$points
}
$outerPoints = Polygon $outer
$cutoutPoints = Polygon $cutout
$sizes = @(16,24,32,48,64,128,256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($n in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($n,$n)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform([single]($n / 1254.0),[single]($n / 1254.0))
    $teal = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#14b1ba'))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    # Soft corner clipping preserves the square app-icon silhouette.
    $background = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $background.AddArc(0,0,342,342,180,90)
    $background.AddArc(912,0,342,342,270,90)
    $background.AddArc(912,912,342,342,0,90)
    $background.AddArc(0,912,342,342,90,90)
    $background.CloseFigure()
    $g.FillPath($teal,$background)
    $g.FillPolygon($white,$outerPoints)
    $g.FillPolygon($teal,$cutoutPoints)
    $stream = [System.IO.MemoryStream]::new()
    $bmp.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    if ($n -eq 256) { $bmp.Save((Join-Path $dir 'ReaKiller.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $g.Dispose(); $bmp.Dispose()
    $teal.Dispose(); $white.Dispose(); $background.Dispose()
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
Write-Host "Generated approved ReaKiller teal icon in $path"
