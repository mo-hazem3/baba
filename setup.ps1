# Gets a fresh copy of the Baba project ready to work on: installs what the web app needs, builds everything and
# checks that the build is healthy. Run it once after copying or cloning the project (double-click setup.cmd).
#
# Needs: Windows 10/11 and the .NET 10 SDK. Node.js comes with the project (in .tools) or from your computer.
# Takes about 5 to 10 minutes the first time.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }

# 1. Tools
Step 'Checking the tools'
$portable = Join-Path $root '.tools\node-v24.21.0-win-x64'
if (Test-Path $portable) { $env:PATH = "$portable;$env:PATH"; Write-Host 'Using the Node.js that comes with the project.' }
foreach ($tool in 'node', 'npm', 'dotnet') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "'$tool' was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download (and Node.js 24 from https://nodejs.org if the .tools folder is missing), then run this again."
    }
}
Write-Host ("Node " + (node --version) + ", .NET SDK " + (dotnet --version))

# 2. The web app's packages
Step 'Installing the web app packages (npm ci)'
Push-Location (Join-Path $root 'web')
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
} finally { Pop-Location }

# 3. The .NET tools (database migrations) and the whole backend
Step 'Restoring the .NET tools'
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

Step 'Building the backend and the desktop app (dotnet build)'
dotnet build (Join-Path $root 'Baba.slnx')
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

# 4. The web app (the desktop app bundles it), then the desktop app again so it picks it up
Step 'Building the web app (npm run build)'
Push-Location (Join-Path $root 'web')
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'npm run build failed.' }
} finally { Pop-Location }

Step 'Building the desktop app with the web app inside'
dotnet build (Join-Path $root 'src\Baba.Desktop')
if ($LASTEXITCODE -ne 0) { throw 'Building the desktop app failed.' }

Write-Host ""
Write-Host 'Everything is ready.' -ForegroundColor Green
Write-Host ''
Write-Host 'Run the app:        src\Baba.Desktop\bin\Debug\net10.0-windows\Baba.Desktop.exe'
Write-Host 'Run all the tests:  dotnet test Baba.slnx      (and, in web\: npm test, npm run e2e)'
Write-Host 'Make the installer: powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1'
Write-Host '                    (the finished file is installer\Baba-Setup-<version>.exe)'
