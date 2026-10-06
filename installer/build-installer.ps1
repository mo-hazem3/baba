# Builds the Baba installer: web app -> published desktop app -> Inno Setup installer.
# Output: artifacts\installer\Baba-Setup-<version>.exe (or a portable zip if Inno Setup is not installed).
# Usage:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# The version comes from Directory.Build.props, so the app and the installer always agree.
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> found in Directory.Build.props.' }
Write-Host "Baba $version"

# 1. The web app. Uses the portable Node in .tools when there is one.
$node = Join-Path $root '.tools\node-v24.21.0-win-x64'
if (Test-Path $node) { $env:PATH = "$node;$env:PATH" }
Push-Location (Join-Path $root 'web')
try {
    if (-not (Test-Path 'node_modules')) { npm ci }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Building the web app failed.' }
} finally { Pop-Location }

# 2. The desktop app, self-contained so users do not need to install .NET.
$publish = Join-Path $root 'artifacts\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\Baba.Desktop') -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publishing the desktop app failed.' }

# 3. The installer (or a portable zip when Inno Setup is missing).
$candidates = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$output = Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory -Force $output | Out-Null

if ($candidates) {
    & ($candidates | Select-Object -First 1) "/DAppVersion=$version" (Join-Path $PSScriptRoot 'Baba.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
    Write-Host "Installer: $output\Baba-Setup-$version.exe"
} else {
    $zip = Join-Path $output "Baba-portable-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
    Write-Warning 'Inno Setup was not found, so a portable zip was made instead (no .baba file association).'
    Write-Host "Portable zip: $zip"
}
