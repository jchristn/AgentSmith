@echo off
rem DESTRUCTIVE: deletes namespace hugo in the minikube profile and redeploys with new secrets. Requires typing RESET.
rem Usage: factory-reset.bat [--with postgres,observability]
setlocal
if "%MINIKUBE_PROFILE%"=="" set MINIKUBE_PROFILE=hugo
set HUGO_KUBE_CONTEXT=%MINIKUBE_PROFILE%
call "%~dp0..\..\k8s\scripts\factory-reset.bat" minikube %*
endlocal
