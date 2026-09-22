#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Version = $(if ($env:ROSLYN_TOOLS_VERSION) { $env:ROSLYN_TOOLS_VERSION } else { "latest" }),
    [string]$BaseUrl = $(if ($env:ROSLYN_TOOLS_BASE_URL) { $env:ROSLYN_TOOLS_BASE_URL } else { "https://github.com/DanMarshall909/roslyn-tools/releases/download" }),
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "RoslynTools"),
    [string]$BinDir = (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps"),
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$resolvedInstallRoot = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\', '/')
$unsafeRoots = @(
    [System.IO.Path]::GetPathRoot($resolvedInstallRoot).TrimEnd('\', '/'),
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile).TrimEnd('\', '/')
)
if ([string]::IsNullOrWhiteSpace($resolvedInstallRoot) -or $unsafeRoots -contains $resolvedInstallRoot) {
    throw "Refusing unsafe install root '$InstallRoot'."
}
$InstallRoot = $resolvedInstallRoot

if ($Uninstall) {
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $BinDir "rt.exe"), (Join-Path $BinDir "roslyn-tools-mcp.exe")
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $InstallRoot
    Write-Host "Removed Roslyn Tools from $InstallRoot"
    exit 0
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$rid = switch ($architecture) {
    "x64" { "win-x64" }
    "arm64" { "win-arm64" }
    default { throw "Unsupported Windows architecture: $architecture" }
}

if ($Version -eq "latest") {
    $release = Invoke-RestMethod "https://api.github.com/repos/DanMarshall909/roslyn-tools/releases/latest"
    $Version = $release.tag_name
}
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw "Invalid version label '$Version'." }

$archive = "roslyn-tools-$Version-$rid.zip"
$downloadUrl = "$($BaseUrl.TrimEnd('/'))/$Version/$archive"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("roslyn-tools-" + [guid]::NewGuid())
$archivePath = Join-Path $tempDir $archive
New-Item -ItemType Directory -Force $tempDir | Out-Null

try {
    Invoke-WebRequest $downloadUrl -OutFile $archivePath
    Invoke-WebRequest "$downloadUrl.sha256" -OutFile "$archivePath.sha256"
    $expected = ((Get-Content "$archivePath.sha256" -Raw) -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 $archivePath).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "Checksum verification failed for $archive" }

    $staging = Join-Path $InstallRoot "versions\.$Version-$rid.tmp.$PID"
    $target = Join-Path $InstallRoot "versions\$Version\$rid"
    New-Item -ItemType Directory -Force $staging | Out-Null
    Expand-Archive -Force $archivePath $staging
    foreach ($name in @("rt.exe", "roslyn-tools-mcp.exe")) {
        if (-not (Test-Path (Join-Path $staging $name))) { throw "Archive does not contain $name" }
    }

    $selfCheck = Join-Path $tempDir "self-check"
    New-Item -ItemType Directory -Force $selfCheck | Out-Null
    Set-Content -LiteralPath (Join-Path $selfCheck "global.json") -Value "{}"
    Push-Location $selfCheck
    try {
        & (Join-Path $staging "rt.exe") inspect doctor --installation-only --directory $selfCheck | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "The staged Roslyn Tools self-check failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $target
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Move-Item -Force $staging $target
    New-Item -ItemType Directory -Force $BinDir | Out-Null
    Copy-Item -Force (Join-Path $target "rt.exe") (Join-Path $BinDir "rt.exe")
    Copy-Item -Force (Join-Path $target "roslyn-tools-mcp.exe") (Join-Path $BinDir "roslyn-tools-mcp.exe")
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $tempDir
    if ($staging) {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $staging
    }
}

Write-Host "Installed Roslyn Tools $Version ($rid)."
Write-Host "Ensure $BinDir is on PATH."
@'
{
  "mcpServers": {
    "roslyn-tools": {
      "command": "roslyn-tools-mcp"
    }
  }
}
'@
