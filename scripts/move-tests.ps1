#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Move test files into category subfolders and realign their namespace
  declarations via `rt fix-namespace`.

.DESCRIPTION
  Reads scripts/test-folders.csv (file, folder). For each row:
    1. git mv tests/RoslynTools.Tests/<file> tests/RoslynTools.Tests/<folder>/<file>
    2. rt fix-namespace --file <new-path> --solution RoslynTools.slnx

  Namespace change: `RoslynTools.Tests` → `RoslynTools.Tests.<Folder>`.
  Sibling helpers (AdhocProjectBuilder, MsBuildFixture) stay in the root
  namespace and are still visible without an explicit using because C#
  name resolution walks parent namespaces.

.PARAMETER Apply
  Actually perform moves. Otherwise prints planned actions only.

.PARAMETER Filter
  Optional substring filter on file name to limit the batch.
#>
param(
    [switch]$Apply,
    [string]$Filter
)

$ErrorActionPreference = 'Stop'
# rt writes progress banners to stderr; under PS 7.3+ that trips NativeCommandError
# and aborts the loop. Disable the stderr-as-error coupling for this script.
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
$csvPath  = Join-Path $PSScriptRoot 'test-folders.csv'
$rtCmd    = Join-Path $repoRoot 'rt.cmd'
$sln      = Join-Path $repoRoot 'RoslynTools.slnx'
$testsDir = Join-Path $repoRoot 'tests\RoslynTools.Tests'

if (-not (Test-Path $csvPath)) { throw "Missing mapping: $csvPath" }
if (-not (Test-Path $rtCmd))   { throw "Missing rt.cmd: $rtCmd" }

$rows = Import-Csv -Path $csvPath
if ($Filter) { $rows = $rows | Where-Object { $_.file -like "*$Filter*" } }

$mode = if ($Apply) { 'APPLY' } else { 'DRY-RUN' }
Write-Host "[$mode] $($rows.Count) file move(s) queued" -ForegroundColor Cyan

$moved = @()

foreach ($row in $rows) {
    $src = Join-Path $testsDir $row.file
    $dstDir = Join-Path $testsDir $row.folder
    $dst = Join-Path $dstDir $row.file

    if ($src -eq $dst) { continue }

    $alreadyMoved = (-not (Test-Path $src)) -and (Test-Path $dst)
    if (-not (Test-Path $src) -and -not $alreadyMoved) {
        Write-Host "  skip (source missing and no destination): $($row.file)" -ForegroundColor DarkYellow
        continue
    }

    if ($alreadyMoved) {
        Write-Host "· $($row.file) already at $($row.folder)/" -ForegroundColor DarkGray
        $moved += ,@{ Path = $dst; Folder = $row.folder }
        continue
    }

    Write-Host "→ $($row.file)  →  $($row.folder)/" -ForegroundColor DarkGray

    if ($Apply) {
        if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir | Out-Null }
        git -C $repoRoot mv $src $dst
        if ($LASTEXITCODE -ne 0) { throw "git mv failed for $src" }
        $moved += ,@{ Path = $dst; Folder = $row.folder }
    }
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "Re-run with -Apply to perform the moves." -ForegroundColor Cyan
    return
}

# rt fix-namespace needs a freshly-built workspace that can see the files at their new paths.
# Build once up front, then fix namespaces in a single pass.
Write-Host ""
Write-Host "Building so rt sees files at new paths..." -ForegroundColor Cyan
dotnet build (Join-Path $repoRoot 'RoslynTools.slnx') -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed before fix-namespace pass" }

$fixOk = 0; $fixFail = 0
foreach ($m in $moved) {
    Write-Host "→ fix-namespace $($m.Path)" -ForegroundColor DarkGray
    # rt writes informational progress to stderr; under PS 5.1 with
    # ErrorActionPreference=Stop that terminates the loop. Route the whole
    # invocation through cmd /c and redirect stderr to nul — $LASTEXITCODE
    # still propagates back via cmd's exit code.
    cmd /c "`"$rtCmd`" fix-namespace --file `"$($m.Path)`" --solution `"$sln`" 2>nul 1>nul"
    if ($LASTEXITCODE -eq 0) { $fixOk++ } else {
        $fixFail++
        Write-Host "  FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Summary: $($moved.Count) moved, $fixOk namespaces fixed, $fixFail failed" -ForegroundColor Cyan
if ($fixFail -gt 0) { exit 1 }
