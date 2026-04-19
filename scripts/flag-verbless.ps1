#!/usr/bin/env pwsh
# One-off: add `# REVIEW (no-verb)` to rows in scripts/test-renames.csv
# whose target_name has no common verb. Already-flagged rows are untouched.
$ErrorActionPreference = 'Stop'
$csv = Join-Path $PSScriptRoot 'test-renames.csv'

$verbs = @(
    'is','are','was','were','be','being','been',
    'does','do','did',
    'has','have','had',
    'returns','returned','throws','threw','fails','failed',
    'adds','removes','creates','updates','deletes','inserts',
    'shows','appears','matches','contains','omits','keeps',
    'writes','reads','produces','generates','receives',
    'becomes','stays','remains','gets','sets',
    'detects','reports','finds','locates','yields',
    'skips','rejects','allows','denies','accepts',
    'rewrites','replaces','renames','moves','inlines','extracts',
    'convert','converts','converted',
    'wraps','strips','trims','splits','joins','maps',
    'builds','loads','opens','closes','applies','ignores',
    'triggers','emits','outputs','prints','logs','notifies',
    'handles','captures','propagates','respects',
    'preserves','pins','anchors',
    'makes','seals','flattens',
    'requires','needs','expects'
)
$verbSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$verbs)

function Test-HasVerb([string]$p) {
    foreach ($w in $p.ToLower().Split('_')) { if ($verbSet.Contains($w)) { return $true } }
    return $false
}

$rows = Import-Csv $csv
$marked = 0
foreach ($r in $rows) {
    if (-not $r.target_name) { continue }
    if ($r.target_name -like '# REVIEW*') { continue }
    if (-not (Test-HasVerb $r.target_name)) {
        $r.target_name = "# REVIEW (no-verb): $($r.target_name)"
        $marked++
    }
}
$rows | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
Write-Host "Marked $marked row(s) as no-verb." -ForegroundColor Yellow
