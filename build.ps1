# Publiceert kniip als zelfstandige single-file exe (geen .NET-installatie nodig op de doel-pc)
# en bouwt de installer als Inno Setup 6 aanwezig is.
# ScreenRecorderLib (screencasts) is een C++/CLI-assembly die niet uit de bundel geladen kan worden;
# IncludeAllContentForSelfExtract pakt daarom bij de eerste start alles uit naar %TEMP%\.net.
#
#
# Elke build levert ook public\KniipTest-<versie>.exe: een testversie met eigen register-, instellingen-
# en mutexnamen (KniipTest), die naast de geïnstalleerde kniip draait zonder diens registratie te raken.
# Oudere versies in public\ worden bij elke build verwijderd.
#
#   .\build.ps1                 -> public\Kniip-Setup-<versie>.exe (indien mogelijk) + testexe
#   .\build.ps1 -Version 1.2.0
#   .\build.ps1 -SkipTest       -> zonder testexe

param(
    [string]$Version = "2.3.2",
    [string]$Runtime = "win-x64",
    [switch]$SkipTest
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\Kniip\Kniip.csproj"
$publishDir = Join-Path $root "obj\publish"
$testPublishDir = Join-Path $root "obj\publish-test"
$outDir = Join-Path $root "public"

# Gebruik een SDK in het gebruikersprofiel als er geen systeembrede SDK is.
$localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
if (-not (dotnet --list-sdks 2>$null) -and (Test-Path (Join-Path $localDotnet "dotnet.exe"))) {
    $env:DOTNET_ROOT = $localDotnet
    $env:PATH = "$localDotnet;$env:PATH"
}

function Publish-Kniip([string]$outputDir, [bool]$test) {
    if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }

    dotnet publish $project `
        -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:Version=$Version `
        -p:KniipTest=$($test.ToString().ToLowerInvariant()) `
        -o $outputDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish is mislukt." }
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Get-ChildItem -Path (Join-Path $outDir "*") -File -Include "Kniip-Setup-*.exe", "KniipTest-*.exe" |
    Remove-Item -Force

Publish-Kniip $publishDir $false

Write-Host "Gepubliceerd: $publishDir\Kniip.exe" -ForegroundColor Green

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $root "installer\Kniip.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer bouwen is mislukt." }
    Write-Host "Installer: $outDir\Kniip-Setup-$Version.exe" -ForegroundColor Green
} else {
    Write-Host "Inno Setup 6 niet gevonden; installer overgeslagen (winget install JRSoftware.InnoSetup)." -ForegroundColor Yellow
}

if (-not $SkipTest) {
    Publish-Kniip $testPublishDir $true
    $testExe = Join-Path $outDir "KniipTest-$Version.exe"
    Copy-Item (Join-Path $testPublishDir "Kniip.exe") $testExe -Force
    Write-Host "Testversie: $testExe" -ForegroundColor Green
}
