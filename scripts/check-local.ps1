#!/usr/bin/env pwsh
param(
    [string]$Solution = "RoslynTools.slnx",
    [string]$TestProject = "tests/RoslynTools.Tests/RoslynTools.Tests.csproj",
    [string]$Filter,
    [string]$Configuration,
    [string]$ResultsDirectory,
    [switch]$Full,
    [switch]$FormatChanged,
    [switch]$Mutation,
    [switch]$SkipBuild,
    [switch]$SkipFormat
)

$ErrorActionPreference = "Stop"

function Get-TrxCount {
    param([System.Xml.XmlElement]$Counters, [string]$Name)

    $attribute = $Counters.GetAttributeNode($Name)
    $value = 0
    if ($null -eq $attribute -or -not [int]::TryParse($attribute.Value, [ref]$value) -or $value -lt 0) {
        throw "missing or invalid $Name count"
    }
    return $value
}

function Assert-CompletedTrx {
    param([string]$Report, [string]$Project)

    try {
        [xml]$document = Get-Content -LiteralPath $Report -Raw
        $root = $document.DocumentElement
        if ($null -eq $root -or $root.LocalName -ne "TestRun") {
            throw "root is not TestRun"
        }
        $summaries = $root.SelectNodes("./*[local-name()='ResultSummary']")
        if ($summaries.Count -ne 1 -or $summaries[0].GetAttribute("outcome") -ne "Completed") {
            throw "test run has no completed result summary"
        }
        $counterNodes = $summaries[0].SelectNodes("./*[local-name()='Counters']")
        if ($counterNodes.Count -ne 1) {
            throw "test run has no unique counters element"
        }
        $counters = [System.Xml.XmlElement]$counterNodes[0]
        $total = Get-TrxCount $counters "total"
        $executed = Get-TrxCount $counters "executed"
        $passed = Get-TrxCount $counters "passed"
        if ($total -eq 0 -or $executed -eq 0 -or $passed -eq 0 -or $passed -gt $executed -or $executed -gt $total) {
            throw "test run has no valid passing tests (total=$total, executed=$executed, passed=$passed)"
        }
        foreach ($name in @("failed", "error", "timeout", "aborted", "passedButRunAborted", "disconnected")) {
            if ((Get-TrxCount $counters $name) -ne 0) {
                throw "test run has nonzero $name count"
            }
        }
        Write-Host "check-local: completed ${Project}: total=$total passed=$passed report=$Report" -ForegroundColor DarkGray
    }
    catch {
        throw "check-local: incomplete test report for ${Project}: ${Report}: $_"
    }
}

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw "Not inside a git repository."
}

Push-Location $repoRoot
$resultsRun = $null
$gatePassed = $false
try {
    $configurationArgs = @()
    if ($Configuration) {
        $configurationArgs = @("-c", $Configuration)
    }
    if (-not $SkipBuild) {
        Write-Host "check-local: build" -ForegroundColor DarkGray
        $buildArgs = @("build", $Solution, "-v", "minimal") + $configurationArgs
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    $testProjects = @($TestProject)
    if ($Full) {
        $solutionProjects = & dotnet sln $Solution list
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $testProjects = @($solutionProjects | Where-Object { $_ -match '^tests[/\\].+\.csproj$' })
        if ($testProjects.Count -eq 0) {
            throw "check-local: no test projects found in $Solution"
        }
    }

    $resultsBase = if ($ResultsDirectory) { [System.IO.Path]::GetFullPath($ResultsDirectory) } else { [System.IO.Path]::GetTempPath() }
    New-Item -ItemType Directory -Path $resultsBase -Force | Out-Null
    $resultsRun = Join-Path $resultsBase ("rt-check-local-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $resultsRun | Out-Null

    for ($index = 0; $index -lt $testProjects.Count; $index++) {
        $project = $testProjects[$index]
        $projectResults = Join-Path $resultsRun ("project-" + ($index + 1))
        New-Item -ItemType Directory -Path $projectResults | Out-Null
        $report = Join-Path $projectResults "results.trx"
        $testArgs = @("test", $project, "-v", "minimal", "--no-build") + $configurationArgs + @(
            "--logger", "trx;LogFileName=results.trx", "--results-directory", $projectResults)
        if ($Filter) {
            $testArgs += @("--filter", $Filter)
        }
        Write-Host "check-local: test $project (report: $report)" -ForegroundColor DarkGray
        & dotnet @testArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Assert-CompletedTrx -Report $report -Project $project
    }

    if (-not $SkipFormat) {
        if ($FormatChanged) {
            & "$PSScriptRoot/format-changed.ps1" -Solution $Solution -Verify
        } else {
            Write-Host "check-local: format verify" -ForegroundColor DarkGray
            dotnet format $Solution --verify-no-changes
        }
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    if ($Mutation) {
        Write-Host "check-local: mutation gate" -ForegroundColor DarkGray
        & "$PSScriptRoot/pre-commit-gates.ps1"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    $gatePassed = $true
}
finally {
    if ($gatePassed -and -not $ResultsDirectory -and $resultsRun) {
        Remove-Item -LiteralPath $resultsRun -Recurse -Force
    }
    Pop-Location
}

if ($gatePassed) {
    Write-Host "check-local: OK" -ForegroundColor Green
}
