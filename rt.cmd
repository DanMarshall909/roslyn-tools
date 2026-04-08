@echo off
dotnet run --project "src\RoslynTools\RoslynTools.csproj" -- %*
exit /b %ERRORLEVEL%
