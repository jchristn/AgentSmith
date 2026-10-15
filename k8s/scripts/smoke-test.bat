@echo off
rem Smoke test of a deployed Hugo (see smoke-test.sh for the checks and environment variables). Runs
rem smoke-test.ps1 with Windows PowerShell; exit code 0 means every check passed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0smoke-test.ps1"
exit /b %ERRORLEVEL%
