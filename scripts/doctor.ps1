#!/usr/bin/env pwsh
param(
    [string]$Solution = "RoslynTools.slnx"
)

$ErrorActionPreference = "Continue"
$failed = $false

function Test-Step([string]$Name, [scriptblock]$Block) {
    Write-Host "doctor: $Name" -ForegroundColor DarkGray
    & $Block
    if ($LASTEXITCODE -ne 0 -or $?) {
        if ($LASTEXITCODE -ne 0) {
            $script:failed = $true
        }
    } else {
        $script:failed = $true
    }
}

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw "Not inside a git repository."
}

Push-Location $repoRoot
try {
    Test-Step "dotnet sdk" { dotnet --version | Out-Host }

    if (-not (Test-Path $Solution)) {
        Write-Host "doctor: missing $Solution" -ForegroundColor Red
        $failed = $true
    }

    $hooksPath = git config --get core.hooksPath
    if ($hooksPath -eq "scripts/git-hooks") {
        Write-Host "doctor: git hooks installed" -ForegroundColor Green
    } else {
        Write-Host "doctor: git hooks not installed; run scripts/install-git-hooks.ps1" -ForegroundColor Yellow
    }

    Test-Step "dotnet tools" { dotnet tool restore }

    $rtScript = if ($IsWindows) { Join-Path $repoRoot "rt.ps1" } else { Join-Path $repoRoot "rt.sh" }
    if (-not (Test-Path $rtScript)) {
        Write-Host "doctor: missing rt wrapper: $rtScript" -ForegroundColor Red
        $failed = $true
    } else {
        Test-Step "rt dump-help" { & $rtScript dump-help > $null }
    }

    if ($failed) {
        Write-Host "doctor: issues found" -ForegroundColor Red
        exit 1
    }

    Write-Host "doctor: OK" -ForegroundColor Green
}
finally {
    Pop-Location
}
