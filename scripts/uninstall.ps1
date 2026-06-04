param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\HdrGuard",
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

$running = Get-Process HdrGuard -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\HdrGuard.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Removed startup shortcut: $shortcutPath"
}

if (Test-Path $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Write-Host "Removed install directory: $InstallDir"
}

$userDataDir = Join-Path $env:APPDATA "HdrGuard"
if ($RemoveUserData -and (Test-Path $userDataDir)) {
    Remove-Item -LiteralPath $userDataDir -Recurse -Force
    Write-Host "Removed user data directory: $userDataDir"
} elseif (Test-Path $userDataDir) {
    Write-Host "Kept user data directory: $userDataDir"
}
