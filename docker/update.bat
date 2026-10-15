@echo off
REM Pull the latest published Hugo images and recreate the stack. Non-destructive: named volumes are preserved.
REM For a destructive reset use factory\reset.bat.
cd /d "%~dp0"
docker compose pull
docker compose down
docker compose up -d
docker ps -a
