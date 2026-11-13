# Haalt de Twemoji-set (emoji-datasource-twitter) en de CLDR-trefwoorden op en schrijft
# een compacte catalogus naar src/Kniip/Assets/Emoji. Alleen opnieuw draaien bij een update.
#
# Twemoji-afbeeldingen: (c) Twitter, Inc en andere bijdragers, CC-BY 4.0.
# Emoji-data: emoji-datasource (MIT). Trefwoorden: Unicode CLDR (Unicode License).

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$datasourceVersion = '15.1.2'
$cldrVersion = '46.0.0'

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src\Kniip\Assets\Emoji'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$datasource = "https://unpkg.com/emoji-datasource-twitter@$datasourceVersion"
$cldr = "https://raw.githubusercontent.com/unicode-org/cldr-json/$cldrVersion/cldr-json"

function Get-Json([string]$url) {
    $bytes = (Invoke-WebRequest -Uri $url -UseBasicParsing).RawContentStream.ToArray()
    [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
}

function Get-Annotations([string]$lang) {
    $map = @{}
    foreach ($kind in 'cldr-annotations-full/annotations', 'cldr-annotations-derived-full/annotationsDerived') {
        $json = Get-Json "$cldr/$kind/$lang/annotations.json"
        $set = if ($json.annotations) { $json.annotations.annotations } else { $json.annotationsDerived.annotations }
        foreach ($p in $set.PSObject.Properties) {
            $key = $p.Name.Replace([string][char]0xFE0F, '')
            $map[$key] = $p.Value
        }
    }
    $map
}

Write-Host 'Spritesheet downloaden...'
Invoke-WebRequest -Uri "$datasource/img/twitter/sheets/64.png" -OutFile (Join-Path $outDir 'twemoji-64.png') -UseBasicParsing

Write-Host 'emoji.json downloaden...'
$emoji = Get-Json "$datasource/emoji.json"

Write-Host 'CLDR-trefwoorden downloaden...'
$nl = Get-Annotations 'nl'
$en = Get-Annotations 'en'

function Get-Char([string]$unified) {
    ($unified -split '-' | ForEach-Object { [char]::ConvertFromUtf32([Convert]::ToInt32($_, 16)) }) -join ''
}

$tones = @{ '1F3FB' = 1; '1F3FC' = 2; '1F3FD' = 3; '1F3FE' = 4; '1F3FF' = 5 }

$items = foreach ($e in $emoji) {
    if (-not $e.has_img_twitter -or $e.obsoleted_by -or $e.category -eq 'Component') { continue }

    $key = (Get-Char $e.unified).Replace([string][char]0xFE0F, '')
    $nlEntry = $nl[$key]
    $enEntry = $en[$key]

    $words = New-Object System.Collections.Generic.List[string]
    $words.AddRange([string[]]$e.short_names)
    if ($e.name) { $words.Add($e.name) }
    foreach ($entry in $nlEntry, $enEntry) {
        if ($entry) {
            if ($entry.tts) { $words.AddRange([string[]]$entry.tts) }
            if ($entry.default) { $words.AddRange([string[]]$entry.default) }
        }
    }

    $skins = $null
    if ($e.skin_variations) {
        $skins = [ordered]@{}
        foreach ($p in $e.skin_variations.PSObject.Properties) {
            $parts = $p.Name -split '-'
            if (($parts | Select-Object -Unique).Count -ne 1 -or -not $tones.ContainsKey($parts[0])) { continue }
            if (-not $p.Value.has_img_twitter) { continue }
            $skins[[string]$tones[$parts[0]]] = @($p.Value.unified.ToLowerInvariant(), $p.Value.sheet_x, $p.Value.sheet_y)
        }
        if ($skins.Count -eq 0) { $skins = $null }
    }

    $nameNl = if ($nlEntry -and $nlEntry.tts) { $nlEntry.tts[0] } else { $null }
    $nameEn = if ($enEntry -and $enEntry.tts) { $enEntry.tts[0] } elseif ($e.name) { $e.name.ToLowerInvariant() } else { $e.short_name }

    $item = [ordered]@{
        u = $e.unified.ToLowerInvariant()
        c = $e.category
        o = $e.sort_order
        x = $e.sheet_x
        y = $e.sheet_y
        s = $e.short_name
        n = if ($nameNl) { $nameNl } else { $nameEn }
        k = (($words | ForEach-Object { $_.ToLowerInvariant().Replace('_', ' ') } | Select-Object -Unique) -join '|')
    }
    if ($skins) { $item['t'] = $skins }
    [pscustomobject]$item
}

$items = $items | Sort-Object o
$json = $items | ConvertTo-Json -Depth 5 -Compress
[IO.File]::WriteAllText((Join-Path $outDir 'emoji.json'), $json, (New-Object Text.UTF8Encoding $false))

Write-Host "Klaar: $(@($items).Count) emoji naar $outDir"
