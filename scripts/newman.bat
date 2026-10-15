@echo off
rem Run the Hugo Postman collection with Newman against a running server.
rem
rem   scripts\newman.bat                      (http://127.0.0.1:8800, seeded passwords)
rem   set HUGO_PASSWORD and HUGO_ADMIN_PASSWORD when you changed the seeded passwords
rem   set HUGO_PORT=8810 and both passwords to HugoDemo2026! for the factory demo stack (docs\FACTORY.md)
rem   set HUGO_MODEL_BASE_URL=http://127.0.0.1:11434 and HUGO_MODEL_NAME to also run model requests
rem
rem Extra arguments are passed to newman (for example --folder MCP). Uses newman from PATH when installed,
rem otherwise npx downloads newman 6. The collection never changes a password (mustChangePassword is advisory,
rem D-048); HUGO_NEW_PASSWORD, when set, is tried as a fallback. Exit code is newman's: 0 when every assertion passes.
setlocal

set "ROOT=%~dp0.."
set "COLLECTION=%ROOT%\assets\postman\Hugo.postman_collection.json"
set "ENVIRONMENT=%ROOT%\assets\postman\Hugo.postman_environment.json"

if "%HUGO_HOST%"=="" set "HUGO_HOST=127.0.0.1"
if "%HUGO_PORT%"=="" set "HUGO_PORT=8800"
if "%HUGO_TENANT_ID%"=="" set "HUGO_TENANT_ID=default"
if "%HUGO_EMAIL%"=="" set "HUGO_EMAIL=anderson@hugo.local"
if "%HUGO_PASSWORD%"=="" set "HUGO_PASSWORD=password"
if "%HUGO_ADMIN_EMAIL%"=="" set "HUGO_ADMIN_EMAIL=admin@hugo.local"
if "%HUGO_ADMIN_PASSWORD%"=="" set "HUGO_ADMIN_PASSWORD=password"
if "%HUGO_MODEL_NAME%"=="" set "HUGO_MODEL_NAME=llama3.1"

set "NEWMAN=newman"
where newman >nul 2>nul
if errorlevel 1 set "NEWMAN=npx --yes newman@6"

%NEWMAN% run "%COLLECTION%" -e "%ENVIRONMENT%" ^
    --env-var "host=%HUGO_HOST%" ^
    --env-var "port=%HUGO_PORT%" ^
    --env-var "tenantId=%HUGO_TENANT_ID%" ^
    --env-var "email=%HUGO_EMAIL%" ^
    --env-var "password=%HUGO_PASSWORD%" ^
    --env-var "adminEmail=%HUGO_ADMIN_EMAIL%" ^
    --env-var "adminPassword=%HUGO_ADMIN_PASSWORD%" ^
    --env-var "newPassword=%HUGO_NEW_PASSWORD%" ^
    --env-var "modelBaseUrl=%HUGO_MODEL_BASE_URL%" ^
    --env-var "modelName=%HUGO_MODEL_NAME%" ^
    --timeout-request 360000 ^
    %*
exit /b %ERRORLEVEL%
