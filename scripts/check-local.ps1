#!/usr/bin/env pwsh
param(
    [string]$Solution = "RoslynTools.slnx",
    [string]$TestProject = "tests/RoslynTools.Tests/RoslynTools.Tests.csproj",
    [string]$Filter,
    [switch]$Full,
    [switch]$FormatChanged,
    [switch]$Mutation
)

$ErrorActionPreference = "Stop"

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw "Not inside a git repository."
}

Push-Location $repoRoot
try {
    Write-Host "check-local: build" -ForegroundColor DarkGray
    dotnet build $Solution -v minimal -p:FormatChanged=false -p:DeployRtSkill=false -p:GenerateCommandsMd=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $testTarget = if ($Full) { $Solution } else { $TestProject }
    $testArgs = @("test", $testTarget, "-v", "minimal", "--no-build")
    if ($Filter) {
        $testArgs += @("--filter", $Filter)
    }

    Write-Host "check-local: test $testTarget" -ForegroundColor DarkGray
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($FormatChanged) {
        & "$PSScriptRoot/format-changed.ps1" -Solution $Solution -Verify
    } else {
        Write-Host "check-local: format verify" -ForegroundColor DarkGray
        dotnet format $Solution --verify-no-changes
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($Mutation) {
        Write-Host "check-local: mutation gate" -ForegroundColor DarkGray
        & "$PSScriptRoot/pre-commit-gates.ps1"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    Write-Host "check-local: OK" -ForegroundColor Green
}
finally {
    Pop-Location
}
