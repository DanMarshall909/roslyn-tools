#!/usr/bin/env pwsh
param(
    [string]$Solution = "RoslynTools.slnx",
    [switch]$Verify
)

$ErrorActionPreference = "Stop"

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw "Not inside a git repository."
}

Push-Location $repoRoot
try {
    if (-not (Test-Path $Solution)) {
        throw "Solution not found: $Solution"
    }

    # Collect .cs files that differ from the last commit (staged + unstaged).
    $diffFiles = @(
        git diff --name-only HEAD
        git diff --name-only --cached
    ) | Sort-Object -Unique | Where-Object { $_ -match '\.cs$' -and (Test-Path $_) }

    if (-not $diffFiles) {
        Write-Host "format-changed: no changed .cs files, skipping." -ForegroundColor DarkGray
        exit 0
    }

    $mode = if ($Verify) { "verifying" } else { "formatting" }
    Write-Host "format-changed: $mode $($diffFiles.Count) file(s)..." -ForegroundColor DarkGray

    $args = @("format", $Solution, "--include") + $diffFiles
    if ($Verify) {
        $args += "--verify-no-changes"
    }

    & dotnet @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
