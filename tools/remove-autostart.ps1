# Haalt kniip uit Windows Opstart-apps voor de aangemelde gebruiker.
# Bedoeld voor wie alleen Kniip.exe heeft gedraaid (geen installer): het pad van de exe
# maakt niet uit, en het bestand hoeft er niet meer te staan.
#
# Elke Windows-account heeft een eigen opstartlijst. Draai dit script in de account
# waarin kniip een keer is gestart. Beheerdersrechten zijn niet nodig.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\remove-autostart.ps1
#   of dubbelklik op remove-autostart.cmd

$ErrorActionPreference = "Stop"

$knownNames = @("Kniip", "KniipTest", "KnipPlus")
$exePattern = '(?i)(^|[\\/"])(Kniip(Test)?(-[\d.]+)?|KnipPlus)\.exe(\b|")'

function Test-KniipCommand([string]$text) {
    return [bool]($text -match $exePattern)
}

function Remove-NamedValue([string]$keyPath, [string]$name) {
    if (-not (Test-Path -LiteralPath $keyPath)) { return $false }
    $key = Get-Item -LiteralPath $keyPath
    if ($key.GetValueNames() -notcontains $name) { return $false }
    Remove-ItemProperty -LiteralPath $keyPath -Name $name
    return $true
}

$removed = New-Object System.Collections.Generic.List[string]
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$approvedRunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
$names = New-Object System.Collections.Generic.HashSet[string]

foreach ($name in $knownNames) { [void]$names.Add($name) }

if (Test-Path -LiteralPath $runKey) {
    $run = Get-Item -LiteralPath $runKey
    foreach ($name in @($run.GetValueNames())) {
        $data = [string]$run.GetValue($name)
        if ($names.Contains($name) -or (Test-KniipCommand $data)) {
            Remove-ItemProperty -LiteralPath $runKey -Name $name
            $removed.Add("Run\$name  ($data)")
            [void]$names.Add($name)
        }
    }
}

foreach ($name in $names) {
    if (Remove-NamedValue $approvedRunKey $name) {
        $removed.Add("StartupApproved\Run\$name")
    }
}

$startup = [Environment]::GetFolderPath("Startup")
$approvedStartupKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
$wsh = New-Object -ComObject WScript.Shell
Get-ChildItem -LiteralPath $startup -Filter "*.lnk" -ErrorAction SilentlyContinue | ForEach-Object {
    $shortcut = $wsh.CreateShortcut($_.FullName)
    $hit = (Test-KniipCommand $shortcut.TargetPath) -or (Test-KniipCommand $shortcut.Arguments) -or ($knownNames -contains $_.BaseName)
    if (-not $hit) { return }
    Remove-Item -LiteralPath $_.FullName -Force
    $removed.Add("Opstartmap\$($_.Name)")
    if (Remove-NamedValue $approvedStartupKey $_.Name) {
        $removed.Add("StartupApproved\StartupFolder\$($_.Name)")
    }
}

if ($removed.Count -eq 0) {
    Write-Host "Geen kniip-opstartregistratie gevonden voor $env:USERNAME."
    exit 0
}

Write-Host "Verwijderd voor ${env:USERNAME}:"
foreach ($line in $removed) { Write-Host "  $line" }
Write-Host ""
Write-Host "kniip staat niet meer in Opstart-apps. Staat de regel er nog, log dan een keer uit en weer in."
