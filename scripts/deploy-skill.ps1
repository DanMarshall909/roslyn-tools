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
$deployed = $false
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        Copy-Item -Path $source -Destination $dest -Force -ErrorAction Stop
        $deployed = $true
        break
    }
    catch {
        if ($attempt -eq 10) {
            # US-075 AC3: graceful-skip path. When the destination is locked
            # by a concurrent build (e.g. rt.cmd / rt.ps1 rebuilding in
            # parallel), don't throw an unhandled IOException -- emit a clear
            # stderr message and exit 0 so the calling build/script keeps going.
            # NOTE: ASCII-only in the message string -- this script is invoked
            # via `powershell` (Windows PowerShell 5.1) from MSBuild, which
            # reads the file as ANSI without a BOM and mangles non-ASCII.
            Write-Error -Message "deploy-skill: destination locked by concurrent build after 10 retries -- skipping deploy. Re-run manually or after the concurrent build settles." -ErrorAction Continue
            exit 0
        }

        Start-Sleep -Milliseconds (100 * $attempt)
    }
}
if ($deployed) {
    Write-Host "Deployed: $dest"
}
