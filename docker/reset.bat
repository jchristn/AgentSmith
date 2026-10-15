@echo off
REM DESTRUCTIVE reset of the Hugo stack in this directory: stops it, deletes every named volume, and starts fresh.
REM The seeded demo stack has its own reset: factory\reset.bat.
cd /d "%~dp0"
set /p answer=This deletes ALL Hugo data in this stack, including stored secrets. Type RESET to continue: 
if not "%answer%"=="RESET" (
  echo Cancelled.
  exit /b 1
)
docker compose --profile postgres down -v --remove-orphans
docker compose up -d
docker ps -a --filter "label=com.docker.compose.project=hugo"
echo Hugo was reset. Log in as anderson@hugo.local / password (tenant default) and change the password.
