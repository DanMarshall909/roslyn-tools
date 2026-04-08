param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

& "$PSScriptRoot\src\RoslynTools\bin\Release\net8.0\win-x64\publish\rt.exe" @Args
exit $LASTEXITCODE
