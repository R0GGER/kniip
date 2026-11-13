# Genereert src/Kniip/AppIcon.ico (16-256 px) met System.Drawing.
# Kleine formaten als 32-bit DIB, 256 px als PNG (standaard ICO-layout).
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\Kniip\AppIcon.ico')
)

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.04))
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
    $radius = $size * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 0, 120, 212),
        [System.Drawing.Color]::FromArgb(255, 0, 84, 166), 90.0)
    $g.FillPath($brush, $path)

    # Gestippeld selectiekader
    $inset = $size * 0.2
    $penW = [Math]::Max(1.0, $size / 16.0)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    if ($size -ge 32) { $pen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash }
    $g.DrawRectangle($pen, $inset, $inset, $size - 2 * $inset, $size * 0.5)

    # Geel markeerstreepje
    $hl = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 214, 0))
    $g.FillRectangle($hl, $inset, $size * 0.74, $size * 0.36, [Math]::Max(1.0, $size * 0.09))

    # Plus-badge
    $badge = $size * 0.36
    $bx = $size - $badge - $pad * 0.5
    $by = $size - $badge - $pad * 0.5
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 17, 35))), $bx, $by, $badge, $badge)
    $pw = [Math]::Max(1.0, $badge * 0.16)
    $plusPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $pw)
    $c = $badge / 2
    $l = $badge * 0.27
    $g.DrawLine($plusPen, $bx + $c - $l, $by + $c, $bx + $c + $l, $by + $c)
    $g.DrawLine($plusPen, $bx + $c, $by + $c - $l, $bx + $c, $by + $c + $l)

    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $px = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$px.B); $bw.Write([byte]$px.G); $bw.Write([byte]$px.R); $bw.Write([byte]$px.A)
        }
    }
    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write([byte[]]::new($maskStride * $h))
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -eq 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        [byte[]]$bytes = $ms.ToArray()
    } else {
        [byte[]]$bytes = Get-DibBytes $bmp
    }
    $images += , $bytes
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Flush()
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($Output), $out.ToArray())
Write-Host "Icoon geschreven naar $Output"
