# Builds, verifies, and publishes the site under gh-pages:/docs.
$ErrorActionPreference = 'Stop'

if ((git branch --show-current) -ne 'main') {
    throw 'Publish from the main branch.'
}

if (git status --porcelain) {
    throw 'Commit or stash repository changes before publishing.'
}

npm test
git pull --ff-only origin main
git push origin main
git fetch origin gh-pages

$deployWorktree = Join-Path ([System.IO.Path]::GetTempPath()) `
    "roslyn-tools-gh-pages-$([Guid]::NewGuid().ToString('N'))"

try {
    git worktree add --detach $deployWorktree origin/gh-pages

    $resolvedWorktree = [System.IO.Path]::GetFullPath($deployWorktree)
    $deployDocs = [System.IO.Path]::GetFullPath(
        (Join-Path $resolvedWorktree 'docs'))

    if (-not $deployDocs.StartsWith(
        $resolvedWorktree + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to replace docs outside the deployment worktree.'
    }

    if (Test-Path -LiteralPath $deployDocs) {
        Remove-Item -LiteralPath $deployDocs -Recurse -Force
    }

    New-Item -ItemType Directory -Path $deployDocs | Out-Null
    Copy-Item -Path (Join-Path $PSScriptRoot 'artifacts/site/*') `
        -Destination $deployDocs -Recurse -Force

    Push-Location $deployWorktree
    try {
        git add -f -A docs
        if (git status --porcelain) {
            git commit -m "Deploy documentation $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            git push origin HEAD:gh-pages --force
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $deployWorktree) {
        git worktree remove $deployWorktree --force
    }
}

Write-Host 'Documentation published to https://roslyn-tools.danmarshall.dev.'
