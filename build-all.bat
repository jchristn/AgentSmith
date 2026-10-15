@echo off
rem Builds and pushes every Hugo image (server, dashboard) for linux/amd64 and linux/arm64, tagged <tag> and latest.
rem Usage: build-all.bat ^<tag^>   (for example v0.1.0)
if "%~1"=="" (
  echo usage: %~nx0 ^<tag^>
  exit /b 2
)
call "%~dp0build-server.bat" %1 || exit /b 1
call "%~dp0build-dashboard.bat" %1 || exit /b 1
