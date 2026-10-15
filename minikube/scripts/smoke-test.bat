@echo off
rem Smoke test of Hugo in minikube. See k8s\scripts\smoke-test.sh for the checks and environment variables.
setlocal
if "%MINIKUBE_PROFILE%"=="" set MINIKUBE_PROFILE=hugo
set HUGO_KUBE_CONTEXT=%MINIKUBE_PROFILE%
call "%~dp0..\..\k8s\scripts\smoke-test.bat"
endlocal & exit /b %ERRORLEVEL%
