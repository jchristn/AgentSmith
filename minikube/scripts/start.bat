@echo off
rem Creates (or starts) the minikube profile for Hugo with the ingress and metrics-server addons. See start.sh.
rem MINIKUBE_PROFILE (default hugo), MINIKUBE_CPUS (4), MINIKUBE_MEMORY (6g), MINIKUBE_DRIVER (minikube's choice).
setlocal
if "%MINIKUBE_PROFILE%"=="" set MINIKUBE_PROFILE=hugo
if "%MINIKUBE_CPUS%"=="" set MINIKUBE_CPUS=4
if "%MINIKUBE_MEMORY%"=="" set MINIKUBE_MEMORY=6g
set DRIVER=
if not "%MINIKUBE_DRIVER%"=="" set DRIVER=--driver=%MINIKUBE_DRIVER%
minikube -p %MINIKUBE_PROFILE% status >nul 2>&1 && (
  echo [hugo minikube] profile %MINIKUBE_PROFILE% is already running
) || (
  minikube start -p %MINIKUBE_PROFILE% --cpus=%MINIKUBE_CPUS% --memory=%MINIKUBE_MEMORY% --addons=ingress --addons=metrics-server %DRIVER% || exit /b 1
)
minikube -p %MINIKUBE_PROFILE% addons enable ingress >nul
minikube -p %MINIKUBE_PROFILE% addons enable metrics-server >nul
echo [hugo minikube] profile %MINIKUBE_PROFILE% is up; next: minikube\scripts\deploy.bat
endlocal
