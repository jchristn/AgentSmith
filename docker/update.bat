@echo off
REM Pull the latest published Hugo images and recreate the stack. Non-destructive: named volumes are preserved.
REM When HUGO_DB_TYPE is Postgresql (in the environment or docker\.env), the postgres profile is included so the
REM PostgreSQL container is pulled and recreated with the rest of the stack.
REM For a destructive reset use factory\reset.bat.
setlocal
cd /d "%~dp0"
set "HUGO_UPDATE_DB_TYPE=%HUGO_DB_TYPE%"
if not defined HUGO_UPDATE_DB_TYPE if exist .env (
  for /f "usebackq eol=# tokens=1,* delims==" %%A in (".env") do (
    if /i "%%A"=="HUGO_DB_TYPE" set "HUGO_UPDATE_DB_TYPE=%%B"
  )
)
set "HUGO_UPDATE_PROFILE="
if /i "%HUGO_UPDATE_DB_TYPE%"=="Postgresql" set "HUGO_UPDATE_PROFILE=--profile postgres"
if /i "%HUGO_UPDATE_DB_TYPE%"=="Postgres" set "HUGO_UPDATE_PROFILE=--profile postgres"
docker compose %HUGO_UPDATE_PROFILE% pull
docker compose %HUGO_UPDATE_PROFILE% down
docker compose %HUGO_UPDATE_PROFILE% up -d
docker ps -a
endlocal
