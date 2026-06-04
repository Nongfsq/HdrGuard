param(
    [string]$InstallDir = "",
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Split-Path -Parent $scriptDir
}

$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)

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
    if ($RemoveUserData) {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
        Write-Host "Removed install directory: $InstallDir"
    } else {
        $removed = 0
        foreach ($relativePath in @("HdrGuard.exe", "config.example.json", "README.txt", "scripts")) {
            $path = Join-Path $InstallDir $relativePath
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Recurse -Force
                $removed++
            }
        }

        Write-Host "Removed application files from: $InstallDir"
        $dataDir = Join-Path $InstallDir "data"
        if (Test-Path -LiteralPath $dataDir) {
            Write-Host "Kept runtime data directory: $dataDir"
        }
    }
}
