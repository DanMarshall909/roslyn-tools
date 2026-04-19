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

$total = $rows.Count
$ok = 0; $skip = 0; $fail = 0
$mode = if ($Apply) { 'APPLY' } else { 'DRY-RUN' }
Write-Host "[$mode] $total rename(s) queued" -ForegroundColor Cyan

foreach ($row in $rows) {
    $old = $row.old_fqn
    $new = $row.new_name
    if (-not $old -or -not $new) { $skip++; continue }

    $args = @('rename', $old, $new, '--solution', $sln)
    if (-not $Apply) { $args += '--dry-run' }

    Write-Host "→ $old  →  $new" -ForegroundColor DarkGray
    & $rtCmd @args 2>&1 | Out-Null
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
