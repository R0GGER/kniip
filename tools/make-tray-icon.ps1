# Maakt src/Kniip/TrayIcon.ico uit de 256px-afbeelding in AppIcon.ico. Het systeemvak tekent op 16px;
# daar zijn de snijlijnen en de zwarte rand op een donkere taakbalk onzichtbaar en lijkt het icoon klein.
# Dit snijdt alleen het fotokader uit, zodat dat het hele vak vult. Opnieuw draaien als AppIcon.ico wijzigt.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\Kniip\AppIcon.ico'
$target = Join-Path $root 'src\Kniip\TrayIcon.ico'

# Kader in de 256px-afbeelding, inclusief de zwarte rand.
$crop = New-Object System.Drawing.Rectangle 42, 42, 172, 172
$sizes = 16, 20, 24, 32, 40, 48, 64

function Get-IcoPng([byte[]]$ico, [int]$size) {
    $count = [BitConverter]::ToUInt16($ico, 4)
    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + $i * 16
        $width = if ($ico[$entry] -eq 0) { 256 } else { $ico[$entry] }
        if ($width -eq $size) {
            $length = [BitConverter]::ToUInt32($ico, $entry + 8)
            $offset = [BitConverter]::ToUInt32($ico, $entry + 12)
            $data = New-Object byte[] $length
            [Array]::Copy($ico, $offset, $data, 0, $length)
            return $data
        }
    }
    throw "Geen ${size}px-afbeelding in $source"
}

$png = Get-IcoPng ([IO.File]::ReadAllBytes($source)) 256
$master = [System.Drawing.Bitmap]::FromStream((New-Object IO.MemoryStream (, $png)))

$images = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($master, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}
$master.Dispose()

$out = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $out
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $writer.Write([byte]$sizes[$i])
    $writer.Write([byte]$sizes[$i])
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) {
    $writer.Write($image)
}
$writer.Flush()
[IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Geschreven: $target"
