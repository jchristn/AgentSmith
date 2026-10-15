@echo off
rem Non-destructive update of Hugo in minikube. See update.sh.
rem Usage: update.bat [--load^|--build] [--with postgres,observability]
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
call "%~dp0images.bat" %MODE% || exit /b 1
if "%WITH%"=="" (
  call "%~dp0..\..\k8s\scripts\update.bat" minikube
) else (
  call "%~dp0..\..\k8s\scripts\update.bat" minikube --with %WITH%
)
endlocal
