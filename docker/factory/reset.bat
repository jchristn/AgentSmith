@echo off
REM DESTRUCTIVE factory reset: stops the Hugo stack, deletes every named volume, and starts fresh.
cd /d "%~dp0\.."
set /p answer=This deletes ALL Hugo data, including stored secrets. Type RESET to continue: 
if not "%answer%"=="RESET" (
  echo Cancelled.
  exit /b 1
)
docker compose --profile postgres down -v
docker compose up -d
docker ps -a
echo Hugo was reset. Log in as anderson@hugo.local / password (tenant default) and change the password.
