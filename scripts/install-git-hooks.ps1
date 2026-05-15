$ErrorActionPreference = "Stop"

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw "Not inside a git repository."
}

Push-Location $repoRoot
try {
    git config core.hooksPath scripts/git-hooks
    Write-Host "Installed git hooks from scripts/git-hooks." -ForegroundColor Green
} finally {
    Pop-Location
}
