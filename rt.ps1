param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

& "$PSScriptRoot\src\RoslynTools.Cli\bin\Release\net8.0\rt.exe" @Args
exit $LASTEXITCODE
