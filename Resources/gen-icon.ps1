# Generates Resources\book.ico (book logo for Study Shell)
param(
    [string]$Dest = (Join-Path $PSScriptRoot "book.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = New-Object System.Collections.Generic.List[byte[]]

foreach ($s in $sizes) {
    $size = [int]$s
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded gradient square (warm academic)
    $pad = [Math]::Max(1, [int]($size * 0.02))
    $w = $size - (2 * $pad)
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, $w, $w)
    $c1 = [System.Drawing.Color]::FromArgb(255, 240, 169, 75)
    $c2 = [System.Drawing.Color]::FromArgb(255, 127, 176, 105)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = $size * 0.22
    $d = 2 * $r
    $x1 = [float]$rect.X; $y1 = [float]$rect.Y
    $x2 = [float]($rect.Right - $d); $y2 = [float]($rect.Bottom - $d)
    $path.AddArc($x1, $y1, $d, $d, 180, 90)
    $path.AddArc($x2, $y1, $d, $d, 270, 90)
    $path.AddArc($x2, $y2, $d, $d, 0, 90)
    $path.AddArc($x1, $y2, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    # open book
    $penW = [Math]::Max(1.0, $size * 0.07)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [float]$penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $cx = $size * 0.5
    $top = $size * 0.28
    $bottom = $size * 0.72
    $left = $size * 0.26
    $right = $size * 0.74

    # left page: arc from center-top to center-bottom
    $g.DrawLine($pen, [float]$left, [float]$top, [float]$cx, [float]$top)
    $g.DrawArc($pen, [float]($left - ($cx - $left)), [float]($top), [float](2 * ($cx - $left)), [float]($bottom - $top), 270, 90)
    # right page
    $g.DrawLine($pen, [float]$cx, [float]$top, [float]$right, [float]$top)
    $g.DrawArc($pen, [float]$cx, [float]($top), [float](2 * ($right - $cx)), [float]($bottom - $top), 270, -90)
    # spine
    $g.DrawLine($pen, [float]$cx, [float]($top - $size * 0.03), [float]$cx, [float]($bottom + $size * 0.03))
    # base line
    $g.DrawLine($pen, [float]$left, [float]$bottom, [float]$cx, [float]$bottom)
    $g.DrawLine($pen, [float]$cx, [float]$bottom, [float]$right, [float]$bottom)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
    $brush.Dispose(); $pen.Dispose(); $path.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = [int]$sizes[$i]
    $len = $pngs[$i].Length
    $bw.Write([byte]($s -band 0xFF))
    $bw.Write([byte]($s -band 0xFF))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$len)
    $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Dest, $out.ToArray())
$bw.Dispose()
$out.Dispose()

Write-Host "Icon created: $Dest" -ForegroundColor Green
