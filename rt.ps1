param(
    [switch] $Raw,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

$exe  = "$PSScriptRoot\src\RoslynTools.Cli\bin\Release\net8.0\rt.exe"
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

if (Test-RebuildNeeded) {
    [Console]::Error.WriteLine('rt: rebuilding...')
    & cmd /c "dotnet build `"$proj`" -c Release --nologo -v quiet 1>&2"
    if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine('rt: build failed'); exit 1 }
    if (Get-Command dotnet-suggest -ErrorAction SilentlyContinue) {
        dotnet-suggest register --command-path $exe 2>$null
    }
}

if ($Raw) {
    & $exe @Args
    # LASTEXITCODE from native exe call propagates to caller naturally
    return
}

$lines = & $exe @Args
$exitCode = $LASTEXITCODE

$text = if ($lines) { ($lines -join "`n").TrimStart() } else { '' }
if ($text.StartsWith('[') -or $text.StartsWith('{')) {
    try { $text | ConvertFrom-Json }
    catch { $lines }
} else {
    $lines
}

# No exit — PowerShell cmdlets don't reset $LASTEXITCODE, so the caller
# sees the exe's exit code without the pipeline being torn down early.
