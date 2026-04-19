#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Batch-rename test methods via `rt rename`, driven by a CSV mapping.

.DESCRIPTION
  Reads scripts/test-renames.csv (source_symbol, scenario, target_name, review),
  calls `rt rename <source_symbol> <target_name> --solution RoslynTools.slnx` for each
  row, and logs the outcome. Runs with --dry-run by default; pass -Apply to
  perform real writes.

  Rows where target_name is blank are skipped (the row exists only for the
  scenario grouping step that follows).

  Refuses to run if any row has a non-empty review column - curate first.

.PARAMETER Apply
  When set, actually performs the rename. Otherwise runs --dry-run.

.PARAMETER Filter
  Optional substring filter on source_symbol to limit the batch.
#>
param(
    [switch]$Apply,
    [string]$Filter
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$csvPath  = Join-Path $PSScriptRoot 'test-renames.csv'
$rtCmd    = Join-Path $repoRoot 'rt.cmd'
$sln      = Join-Path $repoRoot 'RoslynTools.slnx'

if (-not (Test-Path $csvPath)) { throw "Missing mapping: $csvPath" }
if (-not (Test-Path $rtCmd))   { throw "Missing rt.cmd: $rtCmd" }

$rows = @(Import-Csv -Path $csvPath)
if ($Filter) { $rows = @($rows | Where-Object { $_.source_symbol -like "*$Filter*" }) }

# Fail fast on any row with a non-empty review column.
$review = @($rows | Where-Object { $_.review })
if ($review.Count -gt 0) {
    Write-Host "$($review.Count) row(s) still flagged in review column. Curate first." -ForegroundColor Red
    $review | ForEach-Object { Write-Host "  $($_.source_symbol) [$($_.review)]" -ForegroundColor DarkYellow }
    exit 2
}

$total = $rows.Count
$ok = 0; $skip = 0; $fail = 0
$mode = if ($Apply) { 'APPLY' } else { 'DRY-RUN' }
Write-Host "[$mode] $total rename(s) queued" -ForegroundColor Cyan

$index = 0
foreach ($row in $rows) {
    $index++
    $old = $row.source_symbol
    $new = $row.target_name
    if (-not $old) { $skip++; continue }
    # Blank target_name means keep the current method name (the row is present
    # only for the scenario grouping step). Skip the rename.
    if (-not $new) { $skip++; continue }

    Write-Host "[$index/$total] $old  ->  $new" -ForegroundColor DarkGray
    # Route through cmd /c to avoid PS 5.1 converting rt stderr to errors.
    $rtArgs = @('rename', "`"$old`"", "`"$new`"", '--solution', "`"$sln`"")
    if (-not $Apply) { $rtArgs += '--dry-run' }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    cmd /c "`"$rtCmd`" $($rtArgs -join ' ') 2>nul 1>nul"
    $sw.Stop()
    if ($LASTEXITCODE -eq 0) {
        $ok++
        Write-Host ("  OK ({0:n1}s)" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGreen
    } else {
        $fail++
        Write-Host ("  FAILED (exit {0}, {1:n1}s)" -f $LASTEXITCODE, $sw.Elapsed.TotalSeconds) -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Summary: $ok ok, $skip skipped, $fail failed" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 }
Write-Host ""
Write-Host "NOTE: scenario column is NOT applied by this script." -ForegroundColor Cyan
Write-Host "      Run the nested-class restructure step next." -ForegroundColor Cyan
