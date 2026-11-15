# Maakt src/Kniip/AppIconTest.ico en TrayIconTest.ico voor de testbuild (images/kniip_test.svg): dezelfde
# afbeeldingen als AppIcon.ico en TrayIcon.ico, met het blauw (#42a5f5) vervangen door oranje (#ff7e33).
# Elke pixel is een menging van dat blauw met grijs (wit, zwart of #212121), dus het blauwe aandeel volgt
# uit B - R en wordt 1-op-1 omgewisseld; randen en anti-aliasing blijven gelijk.
# Opnieuw draaien als AppIcon.ico of TrayIcon.ico wijzigt.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'src\Kniip'
$blue = 66, 165, 245
$orange = 255, 126, 51

function Convert-Png([byte[]]$png) {
    $bitmap = [System.Drawing.Bitmap]::FromStream((New-Object IO.MemoryStream (, $png)))
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $p = $bitmap.GetPixel($x, $y)
            if ($p.A -eq 0) { continue }
            $t = ($p.B - $p.R) / ($blue[2] - $blue[0])
            if ($t -le 0) { continue }
            $t = [Math]::Min(1.0, $t)
            $c = for ($i = 0; $i -lt 3; $i++) {
                $v = @($p.R, $p.G, $p.B)[$i] + $t * ($orange[$i] - $blue[$i])
                [int][Math]::Max(0, [Math]::Min(255, [Math]::Round($v)))
            }
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($p.A, $c[0], $c[1], $c[2]))
        }
    }
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

foreach ($name in 'AppIcon', 'TrayIcon') {
    $source = Join-Path $dir "$name.ico"
    $target = Join-Path $dir "${name}Test.ico"
    $ico = [IO.File]::ReadAllBytes($source)
    $count = [BitConverter]::ToUInt16($ico, 4)

    $images = for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + $i * 16
        $length = [BitConverter]::ToUInt32($ico, $entry + 8)
        $offset = [BitConverter]::ToUInt32($ico, $entry + 12)
        $data = New-Object byte[] $length
        [Array]::Copy($ico, $offset, $data, 0, $length)
        if ($data[0] -ne 0x89) { throw "Afbeelding $i in $source is geen PNG." }
        , (Convert-Png $data)
    }

    $out = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $out
    $writer.Write($ico, 0, 6)
    $offset = 6 + 16 * $count
    for ($i = 0; $i -lt $count; $i++) {
        $writer.Write($ico, 6 + $i * 16, 8)
        $writer.Write([UInt32]$images[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($target, $out.ToArray())
    Write-Host "Geschreven: $target"
}
