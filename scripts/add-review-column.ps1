#!/usr/bin/env pwsh
# Migrate scripts/test-renames.csv from 3-col (old_fqn, nested_class, new_name)
# to 4-col (old_fqn, nested_class, new_name, review).
# Strips any `# REVIEW (<tag>):` prefix from new_name and writes <tag> to
# the review column. Rows without a marker get an empty review column.
$ErrorActionPreference = 'Stop'
$csv = Join-Path $PSScriptRoot 'test-renames.csv'

$rows = Import-Csv $csv
$out = @()
foreach ($r in $rows) {
    $review = ''
    $name = $r.new_name
    if ($name -match '^# REVIEW \(([^)]+)\):\s*(.+)$') {
        $review = $matches[1]
        $name = $matches[2]
    } elseif ($name -match '^# REVIEW:\s*(.+)$') {
        $review = 'long'
        $name = $matches[1]
    }
    $out += [pscustomobject]@{
        old_fqn      = $r.old_fqn
        nested_class = $r.nested_class
        new_name     = $name
        review       = $review
    }
}
$out | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
Write-Host "Migrated $($out.Count) row(s) to 4-column schema." -ForegroundColor Green
