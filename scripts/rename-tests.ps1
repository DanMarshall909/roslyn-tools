#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Batch-rename test methods via `rt rename`, driven by a CSV mapping.

.DESCRIPTION
  Reads scripts/test-renames.csv (old_fqn, new_name), calls
  `rt rename <old_fqn> <new_name> --solution RoslynTools.sln` for each row,
  and logs the outcome. Runs with --dry-run by default; pass -Apply to
  perform real writes.

  The old_fqn must be the fully-qualified method name as Roslyn sees it
  (e.g. `RoslynTools.Tests.RenameTests.Rename_Type_Updates_Declaration_And_References`).

.PARAMETER Apply
  When set, actually performs the rename. Otherwise runs --dry-run and just
  shows what would change.

.PARAMETER Filter
  Optional substring filter on old_fqn to limit the batch (e.g. 'RenameTests').

.EXAMPLE
  # Preview every rename in the CSV
  .\scripts\rename-tests.ps1

.EXAMPLE
  # Apply renames for just the RenameTests file
  .\scripts\rename-tests.ps1 -Apply -Filter RenameTests

.EXAMPLE
  # Apply everything after review
  .\scripts\rename-tests.ps1 -Apply
#>
param(
    [switch]$Apply,
    [string]$Filter
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$csvPath  = Join-Path $PSScriptRoot 'test-renames.csv'
$rtCmd    = Join-Path $repoRoot 'rt.cmd'
$sln      = Join-Path $repoRoot 'RoslynTools.sln'

if (-not (Test-Path $csvPath)) { throw "Missing mapping: $csvPath" }
if (-not (Test-Path $rtCmd))   { throw "Missing rt.cmd: $rtCmd" }

$rows = Import-Csv -Path $csvPath
if ($Filter) { $rows = $rows | Where-Object { $_.old_fqn -like "*$Filter*" } }

# Fail fast on any row still marked REVIEW — these are awkward compound names
# that need a human pass. The generator flags any row with > 6 words.
$review = $rows | Where-Object { $_.new_name -like '# REVIEW:*' }
if ($review.Count -gt 0) {
    Write-Host "$($review.Count) row(s) still marked '# REVIEW:' — curate them first." -ForegroundColor Red
    $review | ForEach-Object { Write-Host "  $($_.old_fqn)" -ForegroundColor DarkYellow }
    exit 2
}

$total = $rows.Count
$ok = 0; $skip = 0; $fail = 0
$mode = if ($Apply) { 'APPLY' } else { 'DRY-RUN' }
Write-Host "[$mode] $total rename(s) queued" -ForegroundColor Cyan

foreach ($row in $rows) {
    $old = $row.old_fqn
    $new = $row.new_name
    if (-not $old) { $skip++; continue }
    # Blank new_name = keep the current method name (row exists only for
    # the nested_class grouping). Skip the rename call.
    if (-not $new) { $skip++; continue }

    Write-Host "→ $old  →  $new" -ForegroundColor DarkGray
    # Route through cmd /c to avoid PS 5.1 treating rt's stderr progress as errors.
    $rtArgs = @('rename', "`"$old`"", "`"$new`"", '--solution', "`"$sln`"")
    if (-not $Apply) { $rtArgs += '--dry-run' }
    cmd /c "`"$rtCmd`" $($rtArgs -join ' ') 2>nul 1>nul"
    if ($LASTEXITCODE -eq 0) {
        $ok++
    } else {
        $fail++
        Write-Host "  FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Summary: $ok ok, $skip skipped, $fail failed" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 }
Write-Host ""
Write-Host "NOTE: nested_class column is NOT applied by this script." -ForegroundColor Cyan
Write-Host "      rt does not support moving methods to nested classes." -ForegroundColor Cyan
Write-Host "      Run scripts/nest-tests.ps1 afterwards to apply groupings." -ForegroundColor Cyan
