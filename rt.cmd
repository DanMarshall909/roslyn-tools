@echo off
"%~dp0src\RoslynTools\bin\Release\net8.0\win-x64\publish\rt.exe" %*
exit /b %ERRORLEVEL%
