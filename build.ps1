<#
.SYNOPSIS
Builds SimuDeckParser in release mode and packages it into dist\.

.DESCRIPTION
Produces dist\simu_deck_parser-<version>-windows-x86_64.zip containing the
self-contained executable (no .NET needed to run it), README, LICENSE and the
font licence.

Run from PowerShell:
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$name = 'simu_deck_parser'
$version = (Select-String -Path Directory.Build.props -Pattern '<Version>(.+)</Version>' |
    Select-Object -First 1).Matches[0].Groups[1].Value

Write-Host '==> Building for Windows'
$out = Join-Path 'obj' 'publish-win'
dotnet publish SimuDeckParser.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$pkg = "$name-$version-windows-x86_64"
$dir = Join-Path 'dist' $pkg
$zip = "$dir.zip"
if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item (Join-Path $out 'SimuDeckParser.exe') (Join-Path $dir "$name.exe")
# The fonts are embedded in the executable; OFL.txt is their licence.
Copy-Item 'README.md', 'LICENSE', 'fonts\OFL.txt' $dir
Remove-Item -Recurse -Force $out
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $dir -DestinationPath $zip

Write-Host "    $zip"
