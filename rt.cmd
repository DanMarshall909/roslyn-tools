@echo off
"%~dp0src\RoslynTools.Cli\bin\Release\net8.0\rt.exe" %*
exit /b %ERRORLEVEL%
