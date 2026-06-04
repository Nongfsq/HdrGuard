param(
    [string]$InstallDir = "",
    [switch]$Start
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Split-Path -Parent $scriptDir
$sourceExe = Join-Path $packageRoot "HdrGuard.exe"
$runtimeFileNames = @("config.json", "state.json", "HdrGuard.log")

if (-not (Test-Path $sourceExe)) {
    throw "HdrGuard.exe was not found next to this package. Run install.ps1 from an extracted release ZIP."
}

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = $packageRoot
}

$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)

$running = Get-Process HdrGuard -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

New-Item -ItemType Directory -Force $InstallDir | Out-Null

function Copy-IfDifferent {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    $sourceFull = [System.IO.Path]::GetFullPath($SourcePath)
    $destinationFull = [System.IO.Path]::GetFullPath($DestinationPath)
    if ($sourceFull -ieq $destinationFull) {
        return
    }

    Copy-Item -LiteralPath $sourceFull -Destination $destinationFull -Force
}

function Copy-RuntimeFilesIfMissing {
    param(
        [string]$SourceDir,
        [string]$DestinationDir,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $SourceDir)) {
        return
    }

    foreach ($fileName in $runtimeFileNames) {
        $sourcePath = Join-Path $SourceDir $fileName
        $destinationPath = Join-Path $DestinationDir $fileName

        if (-not (Test-Path -LiteralPath $sourcePath) -or (Test-Path -LiteralPath $destinationPath)) {
            continue
        }

        $sourceFull = [System.IO.Path]::GetFullPath($sourcePath)
        $destinationFull = [System.IO.Path]::GetFullPath($destinationPath)
        if ($sourceFull -ieq $destinationFull) {
            continue
        }

        Copy-Item -LiteralPath $sourceFull -Destination $destinationFull -Force
        Write-Host "Migrated $fileName from $Label"
    }
}

Copy-IfDifferent -SourcePath (Join-Path $packageRoot "HdrGuard.exe") -DestinationPath (Join-Path $InstallDir "HdrGuard.exe")
Copy-IfDifferent -SourcePath (Join-Path $packageRoot "config.example.json") -DestinationPath (Join-Path $InstallDir "config.example.json")
if (Test-Path -LiteralPath (Join-Path $packageRoot "README.txt")) {
    Copy-IfDifferent -SourcePath (Join-Path $packageRoot "README.txt") -DestinationPath (Join-Path $InstallDir "README.txt")
}

$installedScripts = Join-Path $InstallDir "scripts"
New-Item -ItemType Directory -Force $installedScripts | Out-Null
foreach ($scriptPath in Get-ChildItem -LiteralPath $scriptDir -Filter "*.ps1") {
    Copy-IfDifferent -SourcePath $scriptPath.FullName -DestinationPath (Join-Path $installedScripts $scriptPath.Name)
}

$dataDir = Join-Path $InstallDir "data"
New-Item -ItemType Directory -Force $dataDir | Out-Null
Copy-RuntimeFilesIfMissing -SourceDir (Join-Path $packageRoot "data") -DestinationDir $dataDir -Label "package data"
Copy-RuntimeFilesIfMissing -SourceDir (Join-Path $env:APPDATA "HdrGuard") -DestinationDir $dataDir -Label "%AppData%\HdrGuard"

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
Write-Host "Runtime data directory: $dataDir"
Write-Host "Startup shortcut: $shortcutPath"

if ($Start) {
    Start-Process -FilePath $targetExe -WorkingDirectory $InstallDir -WindowStyle Hidden
    Write-Host "Started HdrGuard."
}
