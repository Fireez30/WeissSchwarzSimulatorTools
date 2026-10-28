<#
.SYNOPSIS
Installs what is needed to develop and test WeissSchwarz Simulator Tools on Windows.

.DESCRIPTION
Installs (through winget, when missing) Git and the .NET 10 SDK, then
downloads the NuGet packages.

Run from PowerShell:
    powershell -ExecutionPolicy Bypass -File .\setup.ps1
#>
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Install-WingetPackage([string]$Id) {
    Write-Host "==> Installing $Id"
    winget install --id $Id --exact --silent --accept-package-agreements --accept-source-agreements
    # 0 = installed; -1978335189 = already installed / no newer version
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) {
        throw "winget failed to install $Id (exit code $LASTEXITCODE)"
    }
}

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw 'winget is not available. Install "App Installer" from the Microsoft Store, then run this script again.'
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Install-WingetPackage 'Git.Git'
}

$hasSdk = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks) -match '^10\.')
if (-not $hasSdk) {
    Install-WingetPackage 'Microsoft.DotNet.SDK.10'
}
Update-SessionPath

Write-Host '==> Downloading the NuGet packages'
dotnet restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

Write-Host ''
Write-Host 'Done. Next steps (open a new terminal first so PATH is up to date):'
Write-Host '  dotnet run --project WeissSchwarzSimulatorTools.App                  # run a debug build'
Write-Host '  dotnet test                                              # run the tests'
Write-Host '  powershell -ExecutionPolicy Bypass -File .\build.ps1     # release archive in dist\'
Write-Host '  powershell -ExecutionPolicy Bypass -File .\install.ps1   # build and install'
