param(
    [int]$MutationBreakAt = 95,
    [string]$StrykerConfig = "stryker-config.json",
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

if (-not $SelfTest) {
    $mutationEnabled = & node "$PSScriptRoot/mutation-policy.mjs"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($mutationEnabled -eq "false") {
        [Console]::Error.WriteLine("mutation-gate: mutation testing is disabled; skipping.")
        exit 0
    }
}

function Convert-GlobToRegex([string]$glob) {
    $normalized = $glob.Replace("\", "/")

    if ($normalized.StartsWith("!")) {
        $normalized = $normalized.Substring(1)
    }

    $escaped = [regex]::Escape($normalized)
    $escaped = $escaped.Replace("\*\*/", "(.*/)?")
    $escaped = $escaped.Replace("\*\*", ".*")
    $escaped = $escaped.Replace("\*", "[^/]*")

    return "^$escaped$"
}

function Test-GlobMatch([string]$path, [string]$glob) {
    $normalizedPath = $path.Replace("\", "/")
    $regex = Convert-GlobToRegex $glob
    return $normalizedPath -match $regex
}

if ($SelfTest) {
    $cases = @(
        @{ Path = "src/RoslynTools.Cli/Commands/MakeSealedCommand.cs"; Glob = "**/Commands/MakeSealedCommand.cs"; Expected = $true },
        @{ Path = "src/RoslynTools/Core/DependencyInjector.cs"; Glob = "**/Core/DependencyInjector.cs"; Expected = $true },
        @{ Path = "src/RoslynTools/Services/RenameService.cs"; Glob = "**/Core/DependencyInjector.cs"; Expected = $false },
        @{ Path = "tests/RoslynTools.Tests/Mutation/Rename.cs"; Glob = "**/Mutation/Rename.cs"; Expected = $true }
    )

    foreach ($case in $cases) {
        $actual = Test-GlobMatch $case.Path $case.Glob
        if ($actual -ne $case.Expected) {
            Write-Host "mutation-gate self-test failed: '$($case.Path)' vs '$($case.Glob)' expected $($case.Expected), got $actual" -ForegroundColor Red
            exit 1
        }
    }

    Write-Host "mutation-gate self-test passed." -ForegroundColor DarkGray
    exit 0
}

if (-not (Test-Path $StrykerConfig)) {
    Write-Host "mutation-gate: missing $StrykerConfig" -ForegroundColor Red
    exit 1
}

$staged = @(
    git diff --cached --name-only --diff-filter=ACMR
) | Where-Object { $_ }

$productionCs = $staged |
    Where-Object { $_ -match '^src/.+\.cs$' -and $_ -notmatch '(\.g\.cs|\.AssemblyInfo\.cs)$' } |
    Sort-Object -Unique

if (-not $productionCs) {
    Write-Host "mutation-gate: no staged production C# changes, skipping." -ForegroundColor DarkGray
    exit 0
}

$config = Get-Content -Path $StrykerConfig -Raw | ConvertFrom-Json
$mutate = @($config.'stryker-config'.mutate) | Where-Object { $_ -and -not $_.StartsWith("!") }

if (-not $mutate) {
    Write-Host "mutation-gate: $StrykerConfig has no mutate entries." -ForegroundColor Red
    exit 1
}

$covered = @()
$coveredGlobs = @()
$uncovered = @()
foreach ($file in $productionCs) {
    $matchedGlob = $null
    foreach ($glob in $mutate) {
        if (Test-GlobMatch $file $glob) {
            $matchedGlob = $glob
            break
        }
    }

    if ($matchedGlob) {
        $covered += $file
        if ($coveredGlobs -notcontains $matchedGlob) {
            $coveredGlobs += $matchedGlob
        }
    } else {
        $uncovered += $file
    }
}

if ($uncovered -and $env:RT_MUTATION_ALLOW_UNSCOPED -ne "1") {
    Write-Host "mutation-gate: staged production files are not covered by $StrykerConfig mutate globs:" -ForegroundColor Red
    foreach ($file in $uncovered) {
        Write-Host "  - $file" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Add the file(s) to stryker-config.json, or set RT_MUTATION_ALLOW_UNSCOPED=1 for an intentional bypass." -ForegroundColor Yellow
    exit 1
}

if (-not $covered) {
    Write-Host "mutation-gate: no staged production files are in mutation scope, skipping Stryker." -ForegroundColor DarkGray
    exit 0
}

Write-Host "mutation-gate: running Stryker for configured mutation scope." -ForegroundColor DarkGray
foreach ($file in $covered) {
    Write-Host "  - $file" -ForegroundColor DarkGray
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$scopedConfig = $config.'stryker-config'.PSObject.Copy()
$scopedConfig.mutate = $coveredGlobs
$testFilter = & node "$PSScriptRoot/test-lanes.mjs" fast $scopedConfig.'test-case-filter'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$scopedConfig | Add-Member -NotePropertyName "test-case-filter" -NotePropertyValue $testFilter -Force
$scopedWrapper = [pscustomobject]@{ 'stryker-config' = $scopedConfig }
$tempConfig = Join-Path ([System.IO.Path]::GetTempPath()) "stryker-pre-commit-$([guid]::NewGuid()).json"
$scopedWrapper | ConvertTo-Json -Depth 10 | Set-Content -Path $tempConfig -Encoding UTF8

Write-Host "mutation-gate: scoped mutate globs:" -ForegroundColor DarkGray
foreach ($glob in $coveredGlobs) {
    Write-Host "  $glob" -ForegroundColor DarkGray
}

try {
    dotnet stryker `
        --config-file $tempConfig `
        --break-at $MutationBreakAt `
        --threshold-low $MutationBreakAt `
        --threshold-high 100 `
        --reporter progress `
        --reporter cleartext `
        --concurrency 20
    $strykerExit = $LASTEXITCODE
}
finally {
    Remove-Item -Path $tempConfig -Force -ErrorAction SilentlyContinue
}

exit $strykerExit
