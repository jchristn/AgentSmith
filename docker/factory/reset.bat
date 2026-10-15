@echo off
REM DESTRUCTIVE factory reset of the Hugo factory stack (docker\factory\compose.yaml, project hugo-factory).
REM Stops the stack, deletes every factory volume (database, secrets, history, metrics, traces, logs, dashboard
REM state, demo targets), starts it fresh, waits for the seed container, and prints the URLs and demo credentials.
REM The plain stack in docker\ (project hugo) is not touched. docs\FACTORY.md describes what gets seeded.
setlocal
cd /d "%~dp0"
set /p answer=This deletes ALL Hugo factory data, including stored secrets. Type RESET to continue:
if not "%answer%"=="RESET" (
  echo Cancelled.
  exit /b 1
)
docker compose down -v --remove-orphans
if errorlevel 1 exit /b 1
docker compose up -d
if errorlevel 1 exit /b 1
echo Waiting for the seed container (first start pulls images and initializes the databases)...
docker compose wait hugo-seed > nul
set seedstatus=%errorlevel%
docker ps -a --filter "label=com.docker.compose.project=hugo-factory"
echo.
docker compose logs --no-log-prefix hugo-seed
if not "%seedstatus%"=="0" (
  echo The seed container failed. Inspect it with: docker compose -f docker\factory\compose.yaml logs hugo-seed
  exit /b 1
)
echo.
echo Hugo factory stack was reset and seeded.
