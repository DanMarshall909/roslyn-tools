<#
.SYNOPSIS
    Deploy docs/use-rt-skill.md to an AI tool's skills folder.

.PARAMETER DestDir
    Target directory for the skill. Defaults to ~/.claude/skills/use-rt.
    Examples:
      ~/.claude/skills/use-rt          (Claude Code — default)
      ~/openai-plugins/use-rt          (OpenAI plugin dir)
      C:\path\to\custom\skills\use-rt  (any other path)

.EXAMPLE
    .\deploy-skill.ps1
    .\deploy-skill.ps1 -DestDir "$env:USERPROFILE\openai-plugins\use-rt"
#>
param(
    [string]$DestDir = "$env:USERPROFILE\.claude\skills\use-rt"
)

$source = Join-Path $PSScriptRoot "..\docs\use-rt-skill.md"
$dest   = Join-Path $DestDir "SKILL.md"

if (-not (Test-Path $source)) {
    Write-Error "Source not found: $source - run 'dotnet build RoslynTools.slnx' first."
    exit 1
}

New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
Copy-Item -Path $source -Destination $dest -Force
Write-Host "Deployed: $dest"
