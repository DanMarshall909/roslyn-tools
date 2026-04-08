param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

dotnet run --project "src/RoslynTools/RoslynTools.csproj" -- @Args
exit $LASTEXITCODE
