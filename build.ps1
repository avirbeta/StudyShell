# Neon Shell build script
param(
    [string]$Configuration = "Release",
    [switch]$SkipSelfContained
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"

$dotnet = $null
$cmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($cmd) { $dotnet = $cmd.Source }
if (-not $dotnet) {
    $candidates = @(
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe"),
        (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe")
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { $dotnet = $c; break }
    }
    if (-not $dotnet) {
        Write-Host "ERROR: .NET SDK not found. Install .NET 8 SDK." -ForegroundColor Red
        exit 1
    }
}
Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray

$iconPath = Join-Path $root "Resources\neon.ico"
if (-not (Test-Path $iconPath)) {
    Write-Host "Generating icon..." -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "Resources\gen-icon.ps1") -Dest $iconPath
}

Write-Host ""
Write-Host "=== Build Neon Shell ($Configuration) ===" -ForegroundColor Cyan
& $dotnet build (Join-Path $root "StudyShell.csproj") -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "=== Publish: framework-dependent ===" -ForegroundColor Cyan
& $dotnet publish (Join-Path $root "StudyShell.csproj") -c $Configuration -o (Join-Path $dist "framework-dependent")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipSelfContained) {
    Write-Host ""
    Write-Host "=== Publish: self-contained win-x64 ===" -ForegroundColor Cyan
    & $dotnet publish (Join-Path $root "StudyShell.csproj") -c $Configuration -r win-x64 --self-contained true `
        -o (Join-Path $dist "self-contained")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ""
    Write-Host "=== Publish: portable (single EXE) ===" -ForegroundColor Cyan
    & $dotnet publish (Join-Path $root "StudyShell.csproj") -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -o (Join-Path $dist "portable")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Copy-Item (Join-Path $root "README.md") (Join-Path $dist "README.md") -Force

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Portable (single EXE): $dist\portable\StudyShell.exe"
