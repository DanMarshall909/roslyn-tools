param(
    [switch] $Raw,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

$exe  = "$PSScriptRoot\src\RoslynTools.Cli\bin\Release\net10.0\rt.exe"
$proj = "$PSScriptRoot\src\RoslynTools.Cli\RoslynTools.Cli.csproj"

function Test-RebuildNeeded {
    if (-not (Test-Path $exe)) { return $true }
    $t = (Get-Item $exe).LastWriteTime
    if (Get-ChildItem "$PSScriptRoot\src" -Recurse -Include '*.cs','*.csproj' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and $_.LastWriteTime -gt $t } |
        Select-Object -First 1) { return $true }
    if (Get-ChildItem $PSScriptRoot -Filter '*.slnx' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $t } | Select-Object -First 1) { return $true }
    foreach ($f in 'Directory.Build.props','Directory.Packages.props') {
        $p = Join-Path $PSScriptRoot $f
        if ((Test-Path $p) -and (Get-Item $p).LastWriteTime -gt $t) { return $true }
    }
    return $false
}

$mutexName = 'rt-rebuild-' + ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($PSScriptRoot)) -replace '[\\/=+]', '_')
$createdNew = $false
$mutex = [Threading.Mutex]::new($false, $mutexName, [ref]$createdNew)
$hasMutex = $false
try {
    $hasMutex = $mutex.WaitOne([TimeSpan]::FromMinutes(5))
    if (-not $hasMutex) {
        [Console]::Error.WriteLine('rt: timed out waiting for rebuild lock')
        exit 1
    }

    if (Test-RebuildNeeded) {
        [Console]::Error.WriteLine('rt: rebuilding...')
        & cmd /c "dotnet build `"$proj`" -c Release --nologo -v quiet 1>&2"
        if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine('rt: build failed'); exit 1 }
        if (Get-Command dotnet-suggest -ErrorAction SilentlyContinue) {
            dotnet-suggest register --command-path $exe 2>$null
        }
    }
}
finally {
    if ($hasMutex) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}

# Pass-through path. Used when:
#   - the caller explicitly asks for it (-Raw), or
#   - stdout is an interactive terminal (so the child's ANSI markup from
#     Spectre reaches the terminal directly and is not stripped by the
#     PowerShell capture path below).
# Both cases let the child process inherit stdio so colour + syntax
# highlighting render correctly.
if ($Raw -or -not [Console]::IsOutputRedirected) {
    & $exe @Args
    # LASTEXITCODE from native exe call propagates to caller naturally
    return
}

# Capture path. Only reached when stdout is redirected (e.g. `rt foo > out.txt`
# or `rt foo | something`). Spectre sees IsOutputRedirected=true and emits
# plain text — no ANSI to lose. If the output parses as JSON, surface it as
# PowerShell objects so downstream pipeline stages can consume it.
$lines = & $exe @Args

$text = if ($lines) { ($lines -join "`n").TrimStart() } else { '' }
if ($text.StartsWith('[') -or $text.StartsWith('{')) {
    try { $text | ConvertFrom-Json }
    catch { $lines }
} else {
    $lines
}

# No exit — PowerShell cmdlets don't reset $LASTEXITCODE, so the caller
# sees the exe's exit code without the pipeline being torn down early.
