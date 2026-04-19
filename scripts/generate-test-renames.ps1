#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Generate scripts/test-renames.csv from current test method names.
  Produces a mechanical first-cut; expect to hand-edit before applying.

.DESCRIPTION
  Scans tests/RoslynTools.Tests/**/*.cs for [Fact]/[Theory] method
  declarations, writes rows of the form:

      old_fqn,new_name

  The new_name is a mechanical transform:
    - Strip a leading class-name prefix (e.g. `Rename_` in RenameTests)
    - Lowercase everything except the first letter
    - Leave underscores as-is (reads as spaces on most test runners)

  Many rows will need hand-tuning to become actual sentences — this is
  the seed, not the final.
#>
param([switch]$Overwrite)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testsDir = Join-Path $repoRoot 'tests\RoslynTools.Tests'
$out      = Join-Path $PSScriptRoot 'test-renames.csv'

if ((Test-Path $out) -and -not $Overwrite) {
    throw "$out already exists; pass -Overwrite to regenerate."
}

$rows = @()
$files = Get-ChildItem -Path $testsDir -Filter *Tests.cs -Recurse
Write-Host "Scanning $($files.Count) test file(s)..." -ForegroundColor Cyan

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw

    # Detect namespace
    $nsMatch = [regex]::Match($content, 'namespace\s+([\w.]+)\s*;')
    if (-not $nsMatch.Success) { continue }
    $ns = $nsMatch.Groups[1].Value

    # Detect outer class name (first `public class XxxTests` or `public class XxxTests :`)
    $classMatch = [regex]::Match($content, 'public\s+class\s+(\w+Tests?)\b')
    if (-not $classMatch.Success) { continue }
    $className = $classMatch.Groups[1].Value

    # Derive the command prefix to strip: class `RenameTests` → prefix `Rename_`
    $prefix = ($className -replace 'Tests?$', '') + '_'

    # Find test methods: `public async Task X()` or `public void X()` inside the class,
    # preceded anywhere above by [Fact] or [Theory] (not perfect but close enough
    # for a first cut — the user will review the CSV).
    $methodPattern = '(?m)^\s*public\s+(?:async\s+Task|void)\s+(\w+)\s*\('
    foreach ($m in [regex]::Matches($content, $methodPattern)) {
        $method = $m.Groups[1].Value

        # Skip non-test helpers (Dispose, helpers named without the underscore-rich convention)
        if ($method -eq 'Dispose') { continue }

        # Only take methods that are preceded by [Fact] or [Theory] within 10 lines
        $idx = $m.Index
        $before = $content.Substring([Math]::Max(0, $idx - 400), [Math]::Min(400, $idx))
        if ($before -notmatch '\[(Fact|Theory)[^\]]*\]\s*$') { continue }

        $new = $method
        if ($new.StartsWith($prefix)) { $new = $new.Substring($prefix.Length) }

        # Split each underscore-separated chunk into words on PascalCase / digit boundaries,
        # lowercase each word, join with underscores. Keeps existing underscores as separators.
        # Examples:
        #   NoCycle_ReturnsClean  → no_cycle_returns_clean
        #   DryRun_NoWrites       → dry_run_no_writes
        #   AddsFieldAndCtorParam → adds_field_and_ctor_param
        $chunks = $new -split '_'
        $words = @()
        foreach ($chunk in $chunks) {
            if (-not $chunk) { continue }
            # Insert underscores before uppercase letters that follow lowercase or digits.
            $split = [regex]::Replace($chunk, '(?<=[a-z0-9])(?=[A-Z])', '_')
            # Also split on digit→letter: HTTP404ErrorCase → HTTP404_ErrorCase already, fine.
            $words += ($split.Split('_') | ForEach-Object { $_.ToLower() })
        }
        $new = $words -join '_'
        if (-not $new) { continue }
        # Capitalise first letter.
        $new = $new.Substring(0, 1).ToUpper() + $new.Substring(1)
        if ($new -eq $method) { continue }   # no useful change

        $fqn = "$ns.$className.$method"
        $rows += [pscustomobject]@{ old_fqn = $fqn; new_name = $new }
    }
}

$rows | Export-Csv -Path $out -NoTypeInformation -Encoding UTF8
Write-Host "Wrote $($rows.Count) row(s) to $out" -ForegroundColor Green
