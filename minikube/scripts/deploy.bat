@echo off
rem Deploys Hugo to minikube in one step. See deploy.sh.
rem Usage: deploy.bat [--load^|--build] [--with postgres,observability]
setlocal
if "%MINIKUBE_PROFILE%"=="" set MINIKUBE_PROFILE=hugo
set HUGO_KUBE_CONTEXT=%MINIKUBE_PROFILE%
set MODE=pull
set WITH=
:args
if "%~1"=="" goto run
if "%~1"=="--load" set MODE=load
if "%~1"=="--build" set MODE=build
if "%~1"=="--with" (
  set WITH=%~2
  shift
)
shift
goto args
:run
minikube -p %MINIKUBE_PROFILE% status >nul 2>&1 || call "%~dp0start.bat" || exit /b 1
call "%~dp0images.bat" %MODE% || exit /b 1
if "%WITH%"=="" (
  call "%~dp0..\..\k8s\scripts\deploy.bat" minikube || exit /b 1
) else (
  call "%~dp0..\..\k8s\scripts\deploy.bat" minikube --with %WITH% || exit /b 1
)
echo [hugo minikube] next: minikube\scripts\port-forward.bat, then open http://127.0.0.1:8801
endlocal
