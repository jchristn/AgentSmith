@echo off
rem Internal helper for deploy.bat and update.bat: puts the Hugo images into minikube.
rem Usage: images.bat pull^|load^|build   (see deploy.sh)
setlocal
if "%MINIKUBE_PROFILE%"=="" set MINIKUBE_PROFILE=hugo
if "%HUGO_TAG%"=="" set HUGO_TAG=v0.1.0
set REPO=%~dp0..\..
if "%1"=="build" (
  docker build -t jchristn77/hugo-server:%HUGO_TAG% -f "%REPO%\src\Hugo.Server\Dockerfile" "%REPO%" || exit /b 1
  docker build -t jchristn77/hugo-dashboard:%HUGO_TAG% "%REPO%\dashboard" || exit /b 1
)
for %%i in (jchristn77/hugo-server jchristn77/hugo-dashboard) do (
  if "%1"=="pull" (
    echo [hugo minikube] pulling %%i:%HUGO_TAG% into minikube
    minikube -p %MINIKUBE_PROFILE% image pull %%i:%HUGO_TAG% || exit /b 1
  ) else (
    echo [hugo minikube] loading %%i:%HUGO_TAG% from the local Docker daemon
    minikube -p %MINIKUBE_PROFILE% image load --overwrite=true %%i:%HUGO_TAG% || exit /b 1
  )
)
endlocal
