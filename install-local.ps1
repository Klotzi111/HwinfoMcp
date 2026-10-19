#!/usr/bin/env pwsh
#Requires -Version 7.2
# Packs the local build as dotnet tool and (re)installs it globally as 'hwinfo-mcp-dotnet'.
#
# The base version lives in HwinfoMcp/HwinfoMcp.csproj (<VersionPrefix>). This script appends a
# 'local.<timestamp>' suffix, e.g. 0.1.0 -> 0.1.0-local.20261019143000, so local builds are told apart
# from released packages. The timestamp makes every build a new version: NuGet caches packages by
# version, so reinstalling a rebuilt package under the same version would install the cached old build.
#
# Requires PowerShell 7.2 or later (pwsh): Windows PowerShell 5.1 aborts on the stderr output of
# 'dotnet tool uninstall' when the tool isn't installed yet. Also requires the .NET 10 SDK, which
# installing a dotnet tool needs anyway. MCP clients then start the server with the command 'hwinfo-mcp-dotnet'.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$packageId = 'HwinfoMcp'
$command = 'hwinfo-mcp-dotnet'
$project = Join-Path $root 'HwinfoMcp\HwinfoMcp.csproj'
$nupkgDir = Join-Path $root 'artifacts\nupkg'

# Asks MSBuild, so the version is found wherever it is defined (project, Directory.Build.props, ...)
$prefix = (dotnet msbuild $project -getProperty:VersionPrefix | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($prefix)) {
	Write-Error "Could not read VersionPrefix of $project."
	exit 1
}
$suffix = 'local.' + (Get-Date -Format 'yyyyMMddHHmmss')
$version = "$prefix-$suffix"

# A running server keeps its files locked, which makes the uninstall fail half way
$running = Get-Process -Name $command -ErrorAction SilentlyContinue
if ($running) {
	Write-Error ("'$command' is running (PID $($running.Id -join ', ')). Close the MCP clients that use it " +
		"(Claude Desktop, Claude Code sessions) or disconnect it via /mcp, then run this script again.")
	exit 1
}

Write-Host "Packing $packageId $version..." -ForegroundColor Cyan
dotnet pack $project -c Release -o $nupkgDir --version-suffix $suffix
if ($LASTEXITCODE -ne 0) { Write-Error "Pack failed."; exit 1 }

Write-Host "Uninstalling existing global tool $packageId..." -ForegroundColor Cyan
dotnet tool uninstall -g $packageId 2>$null
# Ignore the exit code: the tool may not be installed yet

Write-Host "Installing $packageId $version from the local package..." -ForegroundColor Cyan
dotnet tool install -g $packageId --version $version --add-source $nupkgDir
if ($LASTEXITCODE -ne 0) { Write-Error "Install failed."; exit 1 }

Write-Host "Done. '$command' now runs your local build ($version)." -ForegroundColor Green
