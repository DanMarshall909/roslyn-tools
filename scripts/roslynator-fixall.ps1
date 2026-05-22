#requires -Version 7.0
<#
.SYNOPSIS
    Run Roslynator analyze across the RoslynTools projects and (optionally) fix-all.

.DESCRIPTION
    Roslynator CLI 0.12 cannot parse .slnx (its bundled MSBuild is missing
    MSBuildConstants.InvalidPathChars). This script iterates the five .csproj
    files directly, capturing diagnostics XML + log + summary.md into
    artifacts/roslynator/.

    Default behaviour is analyse-only. Pass -Apply to actually run `roslynator fix`.
    Pass -Verify to also run build / dotnet format / tests after a fix.

.PARAMETER Apply
    When set, runs `roslynator fix` after analyse. Off by default.

.PARAMETER Verify
    When set with -Apply, runs build + format-check + tests after fixing.

.PARAMETER Force
    Required with -Apply when the working tree is dirty. Without it, the
    script refuses to mutate code over uncommitted changes.

.PARAMETER IgnoreDiagnostics
    Comma-separated diagnostic IDs to skip (forwarded to roslynator's
    --ignored-diagnostics on both analyse and fix). Example: "RCS1163,RCS1205"

.PARAMETER SeverityLevel
    Minimum severity. Defaults to info (everything). Allowed: hidden, info, warning, error.

.PARAMETER MaxIterations
    Passed to `roslynator fix --max-iterations`. Default 5.

.EXAMPLE
    pwsh -NoProfile -File scripts/roslynator-fixall.ps1
    Analyse-only. Writes artifacts/roslynator/{diagnostics.xml, analyze.log, summary.md}.

.EXAMPLE
    pwsh -NoProfile -File scripts/roslynator-fixall.ps1 -Apply -Verify
    Analyse, fix, then build + format-check + test. Requires clean working tree (or -Force).

.EXAMPLE
    pwsh -NoProfile -File scripts/roslynator-fixall.ps1 -Apply -IgnoreDiagnostics "RCS1163,SYSLIB1045"
    Skip "unused parameter" and "convert to GeneratedRegex" fixers.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Verify,
    [switch]$Force,
    [string]$IgnoreDiagnostics = "",
    [ValidateSet("hidden", "info", "warning", "error")]
    [string]$SeverityLevel = "info",
    [int]$MaxIterations = 5
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifactDir = Join-Path $repoRoot "artifacts\roslynator"
$null = New-Item -ItemType Directory -Force -Path $artifactDir

$projects = @(
    "src\RoslynTools\RoslynTools.csproj",
    "src\RoslynTools.Cli\RoslynTools.Cli.csproj",
    "src\RoslynTools.Mcp\RoslynTools.Mcp.csproj",
    "tests\RoslynTools.Tests\RoslynTools.Tests.csproj",
    "tests\RoslynTools.Tests.Mcp\RoslynTools.Tests.Mcp.csproj"
) | ForEach-Object { Join-Path $repoRoot $_ }

# --- preflight ---------------------------------------------------------------

if (-not (Get-Command roslynator -ErrorAction SilentlyContinue)) {
    Write-Error "roslynator CLI not found on PATH. Install with: dotnet tool install -g roslynator.dotnet.cli"
}

if ($Apply -and -not $Force) {
    $dirty = (& git -C $repoRoot status --porcelain) -join "`n"
    if ($dirty) {
        Write-Error @"
Working tree is dirty. Refusing to apply fixes over uncommitted changes.
Either commit/stash first, or re-run with -Force.

Dirty paths:
$dirty
"@
    }
}

$ignoredArgs = @()
if ($IgnoreDiagnostics.Trim()) {
    $ids = $IgnoreDiagnostics -split "[,\s]+" | Where-Object { $_ }
    $ignoredArgs = @("--ignored-diagnostics") + $ids
}

# --- phase 1: analyse --------------------------------------------------------

Write-Host "[1/3] roslynator analyze ($($projects.Count) projects, severity=$SeverityLevel)" -ForegroundColor Cyan

$analyzeArgs = @(
    "analyze"
) + $projects + @(
    "--severity-level", $SeverityLevel,
    "--output", (Join-Path $artifactDir "diagnostics.xml"),
    "--file-log", (Join-Path $artifactDir "analyze.log"),
    "--file-log-verbosity", "normal",
    "--verbosity", "minimal"
) + $ignoredArgs

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& roslynator @analyzeArgs
$analyzeExit = $LASTEXITCODE
$sw.Stop()
Write-Host "  analyze exit=$analyzeExit elapsed=$($sw.Elapsed)" -ForegroundColor DarkGray

# roslynator analyze returns 1 when diagnostics are found (not a failure for us).
# Anything other than 0 or 1 is a real error.
if ($analyzeExit -notin 0, 1) {
    Write-Error "roslynator analyze failed (exit=$analyzeExit). See $artifactDir\analyze.log"
}

# --- summary.md --------------------------------------------------------------

$xmlPath = Join-Path $artifactDir "diagnostics.xml"
if (-not (Test-Path $xmlPath)) {
    Write-Warning "No diagnostics.xml produced — clean slate or analyser failed silently."
    return
}

[xml]$x = Get-Content $xmlPath
$rows = @($x.Roslynator.CodeAnalysis.Summary.Diagnostic |
    Select-Object @{N='Id';E={$_.Id}}, @{N='Count';E={[int]$_.Count}}, @{N='Title';E={$_.Title}} |
    Sort-Object Count -Descending)
$total = ($rows | Measure-Object -Property Count -Sum).Sum
$lines = @(
    "# Roslynator diagnostics — $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    ""
    "Source: ``artifacts/roslynator/diagnostics.xml`` (severity-level=$SeverityLevel, $($projects.Count) projects)."
    ""
    "**Total diagnostics: $total** across **$($rows.Count)** distinct rules."
    ""
    "| Id | Count | Title |"
    "| --- | ---: | --- |"
)
foreach ($r in $rows) {
    $lines += "| $($r.Id) | $($r.Count) | $($r.Title) |"
}
$summaryPath = Join-Path $artifactDir "summary.md"
$lines | Set-Content -Path $summaryPath -Encoding utf8
Write-Host "  wrote $summaryPath ($($rows.Count) rules, $total diagnostics)" -ForegroundColor DarkGray

if (-not $Apply) {
    Write-Host ""
    Write-Host "Analyse-only run complete. Review $summaryPath, then re-run with -Apply to fix." -ForegroundColor Green
    exit 0
}

# --- phase 2: fix ------------------------------------------------------------

Write-Host ""
Write-Host "[2/3] roslynator fix (max-iterations=$MaxIterations, format=on)" -ForegroundColor Cyan

$fixArgs = @(
    "fix"
) + $projects + @(
    "--severity-level", $SeverityLevel,
    "--max-iterations", $MaxIterations,
    "--format",
    "--file-log", (Join-Path $artifactDir "fix.log"),
    "--file-log-verbosity", "normal",
    "--verbosity", "minimal"
) + $ignoredArgs

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& roslynator @fixArgs
$fixExit = $LASTEXITCODE
$sw.Stop()
Write-Host "  fix exit=$fixExit elapsed=$($sw.Elapsed)" -ForegroundColor DarkGray

if ($fixExit -ne 0) {
    Write-Error "roslynator fix failed (exit=$fixExit). See $artifactDir\fix.log"
}

# --- phase 3: verify ---------------------------------------------------------

if (-not $Verify) {
    Write-Host ""
    Write-Host "Fix applied. Run -Verify (or manually: dotnet build / dotnet format --verify-no-changes / dotnet test) before committing." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[3/3] verify: build / format / test" -ForegroundColor Cyan

$slnx = Join-Path $repoRoot "RoslynTools.slnx"
$testProj = Join-Path $repoRoot "tests\RoslynTools.Tests\RoslynTools.Tests.csproj"

$steps = @(
    @{ Name = "dotnet build";        Cmd = { & dotnet build $slnx } },
    @{ Name = "dotnet format check"; Cmd = { & dotnet format $slnx --verify-no-changes } },
    @{ Name = "dotnet test";         Cmd = { & dotnet test $testProj -v minimal } }
)

foreach ($step in $steps) {
    Write-Host "  -> $($step.Name)" -ForegroundColor DarkGray
    & $step.Cmd
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$($step.Name) failed (exit=$LASTEXITCODE). Stopping."
    }
}

Write-Host ""
Write-Host "All verification steps passed. Review with 'git diff' and commit on a dedicated branch." -ForegroundColor Green
