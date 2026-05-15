<#
.SYNOPSIS
    Deploy skills/use-rt/SKILL.md to an AI tool's skills folder.

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

$source = Join-Path $PSScriptRoot "..\skills\use-rt\SKILL.md"
$dest   = Join-Path $DestDir "SKILL.md"

if (-not (Test-Path $source)) {
    Write-Error "Source not found: $source"
    exit 1
}

New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        Copy-Item -Path $source -Destination $dest -Force -ErrorAction Stop
        break
    }
    catch {
        if ($attempt -eq 10) {
            throw
        }

        Start-Sleep -Milliseconds (100 * $attempt)
    }
}
Write-Host "Deployed: $dest"
