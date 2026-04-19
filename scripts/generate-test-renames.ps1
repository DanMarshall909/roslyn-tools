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

# Default nested_class scenario per test class. Rows get this as a seed;
# you refine where a file has genuinely distinct sub-scenarios (e.g.
# ExtractMethod splits "extracting into new method" vs "replacing duplicates").
$DefaultScenarios = @{
    'AddDependencyTests'             = 'When_adding_a_constructor_dependency'
    'AddNullChecksTests'             = 'When_adding_null_checks'
    'AddTestTests'                   = 'When_adding_a_test_method'
    'CircularDepsTests'              = 'When_detecting_circular_dependencies'
    'ConvertToRecordTests'           = 'When_converting_a_class_to_a_record'
    'DependencyGraphTests'           = 'When_generating_a_dependency_graph'
    'ExtractClassTests'              = 'When_extracting_a_class'
    'ExtractInterfaceTests'          = 'When_extracting_an_interface'
    'ExtractMethodTests'             = 'When_extracting_a_method'
    'FindCallersTests'               = 'When_finding_callers'
    'FindImplementationsTests'       = 'When_finding_implementations'
    'FindPatternTests'               = 'When_finding_a_pattern'
    'FindRefsTests'                  = 'When_finding_references'
    'FindUnusedTests'                = 'When_finding_unused_symbols'
    'FixNamespaceTests'              = 'When_fixing_a_namespace'
    'ImplementInterfaceTests'        = 'When_implementing_an_interface'
    'InlineMethodTests'              = 'When_inlining_a_method'
    'InspectTests'                   = 'When_inspecting_a_symbol'
    'ListSmellsTests'                = 'When_listing_code_smells'
    'MakeAsyncTests'                 = 'When_making_a_method_async'
    'MakeInstanceTests'              = 'When_making_a_method_instance'
    'MakeSealedTests'                = 'When_sealing_a_type'
    'MakeStaticTests'                = 'When_making_a_method_static'
    'MoveTests'                      = 'When_moving_a_type'
    'OutlineTests'                   = 'When_outlining_a_file'
    'RenameTests'                    = 'When_renaming_a_symbol'
    'ReplaceCallTests'               = 'When_replacing_a_method_call'
    'ReplaceTypeCommandTests'        = 'When_replacing_a_type'
    'SafeDeleteTests'                = 'When_safely_deleting_a_symbol'
    'SolutionWriterIntegrationTests' = 'When_applying_solution_changes'
    'SolutionWriterTests'            = 'When_printing_a_diff'
    'SymbolFormatterTests'           = 'When_formatting_a_symbol'
    'TransformParametersTests'       = 'When_transforming_parameters'
    'WorkspaceLoaderSmokeTests'      = 'When_loading_a_real_solution'
}

# Coarse verb detector. Returns $true if the snake_case phrase contains at
# least one common English verb as a standalone word. Flags cases like
# `Cycle_path_in_output` (no verb) so the user rewrites them — e.g. to
# `Cycle_path_appears_in_output`.
$CommonVerbs = @(
    'is','are','was','were','be','being','been',
    'does','do','did','doesnt','dont','didnt',
    'has','have','had','hasnt','havent',
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
) | ForEach-Object { $_.ToLower() } | Sort-Object -Unique
$VerbSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$CommonVerbs)

function Test-HasVerb {
    param([string]$Phrase)
    foreach ($word in $Phrase.ToLower().Split('_')) {
        if ($VerbSet.Contains($word)) { return $true }
    }
    return $false
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

        # Keep rows even when the mechanical rename is a no-op — they may
        # still need a nested_class grouping.
        $newName = $result.Name
        if ($newName -eq $method) { $newName = '' }   # blank = leave method name as-is
        else {
            $issues = @()
            if ($result.WordCount -gt 6) { $issues += 'long' }
            if (-not (Test-HasVerb $newName)) { $issues += 'no-verb' }
            if ($issues.Count -gt 0) {
                $newName = "# REVIEW ($($issues -join ',')): $newName"
            }
        }

        $nested = if ($DefaultScenarios.ContainsKey($className)) { $DefaultScenarios[$className] } else { '' }

        $rows += [pscustomobject]@{
            old_fqn      = "$ns.$className.$method"
            nested_class = $nested
            new_name     = $newName
        }
    }
}

$rows | Export-Csv -Path $out -NoTypeInformation -Encoding UTF8
Write-Host "Wrote $($rows.Count) row(s) to $out" -ForegroundColor Green
$reviewCount = ($rows | Where-Object { $_.new_name -like '# REVIEW:*' }).Count
Write-Host "$reviewCount row(s) marked for REVIEW (> 6 words)" -ForegroundColor Yellow
