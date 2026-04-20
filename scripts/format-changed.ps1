param([string]$Solution)

# Collect .cs files that differ from the last commit (staged + unstaged)
# plus any files ahead of origin/main that aren't yet on the remote.
$diffFiles = @(
    git diff --name-only HEAD
    git diff --name-only --cached
) | Sort-Object -Unique | Where-Object { $_ -match '\.cs$' -and (Test-Path $_) }

if (-not $diffFiles) {
    Write-Host "format-changed: no changed .cs files, skipping." -ForegroundColor DarkGray
    exit 0
}

$include = $diffFiles -join ' '
Write-Host "format-changed: formatting $($diffFiles.Count) file(s)..." -ForegroundColor DarkGray
dotnet format $Solution --include $include
exit $LASTEXITCODE
