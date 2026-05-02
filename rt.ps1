param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

$exe  = "$PSScriptRoot\src\RoslynTools.Cli\bin\Release\net8.0\rt.exe"
$proj = "$PSScriptRoot\src\RoslynTools.Cli\RoslynTools.Cli.csproj"

function Test-RebuildNeeded {
    if (-not (Test-Path $exe)) { return $true }
    $t = (Get-Item $exe).LastWriteTime
    if (Get-ChildItem "$PSScriptRoot\src" -Recurse -Include '*.cs','*.csproj' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $t } | Select-Object -First 1) { return $true }
    if (Get-ChildItem $PSScriptRoot -Filter '*.slnx' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $t } | Select-Object -First 1) { return $true }
    foreach ($f in 'Directory.Build.props','Directory.Packages.props') {
        $p = Join-Path $PSScriptRoot $f
        if ((Test-Path $p) -and (Get-Item $p).LastWriteTime -gt $t) { return $true }
    }
    return $false
}

if (Test-RebuildNeeded) {
    Write-Host 'rt: rebuilding...' -ForegroundColor Cyan
    dotnet build $proj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Write-Error 'rt: build failed'; exit 1 }
}

& $exe @Args
exit $LASTEXITCODE
