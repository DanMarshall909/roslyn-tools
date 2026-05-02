@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0rt.ps1" %*
exit /b %ERRORLEVEL%
