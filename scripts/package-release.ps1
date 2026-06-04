param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..")
$artifactRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot
} else {
    Join-Path $repoRoot $OutputRoot
}
$publishDir = Join-Path $artifactRoot "publish\$Runtime"
$packageDir = Join-Path $artifactRoot "package\HdrGuard-$Runtime"
$zipPath = Join-Path $artifactRoot "HdrGuard-$Runtime.zip"
$shaPath = "$zipPath.sha256"

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $packageDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $shaPath -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $publishDir | Out-Null
New-Item -ItemType Directory -Force $packageDir | Out-Null

dotnet publish (Join-Path $repoRoot "src\HdrGuard\HdrGuard.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $publishDir

Copy-Item -LiteralPath (Join-Path $publishDir "HdrGuard.exe") -Destination $packageDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "src\HdrGuard\config.example.json") -Destination $packageDir -Force

$scriptsDir = Join-Path $packageDir "scripts"
New-Item -ItemType Directory -Force $scriptsDir | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\install.ps1") -Destination $scriptsDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\uninstall.ps1") -Destination $scriptsDir -Force

@"
HdrGuard $Version

Run HdrGuard.exe directly, or install it at login:

    powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -Start

Uninstall while keeping user config:

    powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1

Runtime files are stored under %AppData%\HdrGuard.
"@ | Set-Content -Path (Join-Path $packageDir "README.txt") -Encoding UTF8

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

$hash = Get-FileHash -Algorithm SHA256 $zipPath
"$($hash.Hash)  $(Split-Path -Leaf $zipPath)" | Set-Content -Path $shaPath -Encoding ASCII

Write-Host "Created $zipPath"
Write-Host "Created $shaPath"
