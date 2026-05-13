#!/usr/bin/env pwsh
# Run once to wire up rt tab completion for your shell.
# Works on Windows (PowerShell), Linux, and macOS (pwsh).

$repoRoot = Split-Path $PSScriptRoot -Parent
$exe      = Join-Path $repoRoot "src" "RoslynTools.Cli" "bin" "Release" "net8.0" "rt.exe"

# Build if the binary doesn't exist yet
if (-not (Test-Path $exe)) {
    Write-Host "rt: binary not found, building..."
    $proj = Join-Path $repoRoot "src" "RoslynTools.Cli" "RoslynTools.Cli.csproj"
    dotnet build $proj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }
}

# Install dotnet-suggest if missing
if (-not (Get-Command dotnet-suggest -ErrorAction SilentlyContinue)) {
    Write-Host "Installing dotnet-suggest..."
    dotnet tool install -g dotnet-suggest
    if ($LASTEXITCODE -ne 0) { Write-Error "Failed to install dotnet-suggest"; exit 1 }
}

# Register the binary (idempotent)
dotnet-suggest register --command-path $exe
Write-Host "Registered: $exe"

# Detect shell and emit profile snippet
if ($IsLinux -or $IsMacOS) {
    $shellName  = [System.IO.Path]::GetFileName($env:SHELL ?? 'bash')
    $profilePath = if ($shellName -eq 'zsh') { "~/.zshrc" } else { "~/.bashrc" }
} else {
    $shellName   = 'pwsh'
    $profilePath = $PROFILE
}

$snippet = dotnet-suggest script $shellName

Write-Host ""
Write-Host "Add the following to $profilePath, then restart your shell:"
Write-Host ""
Write-Host $snippet
