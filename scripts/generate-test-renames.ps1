#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Generate scripts/test-renames.csv from current test method names.
  Produces a mechanical first-cut with three columns; expect hand-tuning.

.DESCRIPTION
  Scans tests/RoslynTools.Tests/**/*.cs for [Fact]/[Theory] method
  declarations, writes rows of the form:

      old_fqn,nested_class,new_name

  - old_fqn: fully-qualified method name (RoslynTools.Tests.Foo.Bar.Baz)
  - nested_class: LEFT BLANK. Hand-fill with `When_<scenario>` grouping
    (e.g. "When_renaming_a_type"). Empty means the method stays at the
    outer class level.
  - new_name: mechanical sentence-case seed. Rows that end up with
    > 6 underscore-separated words get a `# REVIEW:` prefix so
    awkward compound names are easy to spot.

  Many rows will need hand-tuning to become actual business sentences.
#>
param([switch]$Overwrite)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testsDir = Join-Path $repoRoot 'tests\RoslynTools.Tests'
$out      = Join-Path $PSScriptRoot 'test-renames.csv'

if ((Test-Path $out) -and -not $Overwrite) {
    throw "$out already exists; pass -Overwrite to regenerate."
}

function Convert-ToSnakeSentence {
    param([string]$Name, [string]$ClassPrefix)

    $trim = $Name
    if ($trim.StartsWith($ClassPrefix)) { $trim = $trim.Substring($ClassPrefix.Length) }

    $chunks = $trim -split '_'
    $words = @()
    foreach ($chunk in $chunks) {
        if (-not $chunk) { continue }
        $split = [regex]::Replace($chunk, '(?<=[a-z0-9])(?=[A-Z])', '_')
        $words += ($split.Split('_') | Where-Object { $_ } | ForEach-Object { $_.ToLower() })
    }
    if (-not $words) { return $null }

    $joined = $words -join '_'
    $joined = $joined.Substring(0, 1).ToUpper() + $joined.Substring(1)
    return @{ Name = $joined; WordCount = $words.Count }
}

$rows = @()
$files = Get-ChildItem -Path $testsDir -Filter *Tests.cs -Recurse
Write-Host "Scanning $($files.Count) test file(s)..." -ForegroundColor Cyan

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw

    $nsMatch = [regex]::Match($content, 'namespace\s+([\w.]+)\s*;')
    if (-not $nsMatch.Success) { continue }
    $ns = $nsMatch.Groups[1].Value

    $classMatch = [regex]::Match($content, 'public\s+class\s+(\w+Tests?)\b')
    if (-not $classMatch.Success) { continue }
    $className = $classMatch.Groups[1].Value
    $prefix = ($className -replace 'Tests?$', '') + '_'

    $methodPattern = '(?m)^\s*public\s+(?:async\s+Task|void)\s+(\w+)\s*\('
    foreach ($m in [regex]::Matches($content, $methodPattern)) {
        $method = $m.Groups[1].Value
        if ($method -eq 'Dispose') { continue }

        $idx = $m.Index
        $before = $content.Substring([Math]::Max(0, $idx - 400), [Math]::Min(400, $idx))
        if ($before -notmatch '\[(Fact|Theory)[^\]]*\]\s*$') { continue }

        $result = Convert-ToSnakeSentence -Name $method -ClassPrefix $prefix
        if (-not $result) { continue }
        if ($result.Name -eq $method) { continue }

        $newName = $result.Name
        if ($result.WordCount -gt 6) {
            $newName = "# REVIEW: $newName"
        }

        $rows += [pscustomobject]@{
            old_fqn      = "$ns.$className.$method"
            nested_class = ''
            new_name     = $newName
        }
    }
}

$rows | Export-Csv -Path $out -NoTypeInformation -Encoding UTF8
Write-Host "Wrote $($rows.Count) row(s) to $out" -ForegroundColor Green
$reviewCount = ($rows | Where-Object { $_.new_name -like '# REVIEW:*' }).Count
Write-Host "$reviewCount row(s) marked for REVIEW (> 6 words)" -ForegroundColor Yellow
