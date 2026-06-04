param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\HdrGuard",
    [switch]$Start
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Split-Path -Parent $scriptDir
$sourceExe = Join-Path $packageRoot "HdrGuard.exe"

if (-not (Test-Path $sourceExe)) {
    throw "HdrGuard.exe was not found next to this package. Run install.ps1 from an extracted release ZIP."
}

$running = Get-Process HdrGuard -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

New-Item -ItemType Directory -Force $InstallDir | Out-Null

Copy-Item -Path (Join-Path $packageRoot "HdrGuard.exe") -Destination $InstallDir -Force
Copy-Item -Path (Join-Path $packageRoot "config.example.json") -Destination $InstallDir -Force

$installedScripts = Join-Path $InstallDir "scripts"
New-Item -ItemType Directory -Force $installedScripts | Out-Null
Copy-Item -Path (Join-Path $scriptDir "*.ps1") -Destination $installedScripts -Force

$startupDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
New-Item -ItemType Directory -Force $startupDir | Out-Null
$shortcutPath = Join-Path $startupDir "HdrGuard.lnk"
$targetExe = Join-Path $InstallDir "HdrGuard.exe"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = "Start HdrGuard at login"
$shortcut.Save()

Write-Host "Installed HdrGuard to $InstallDir"
Write-Host "Startup shortcut: $shortcutPath"

if ($Start) {
    Start-Process -FilePath $targetExe -WorkingDirectory $InstallDir -WindowStyle Hidden
    Write-Host "Started HdrGuard."
}
