@echo off
rem DESTRUCTIVE: deletes the whole minikube profile. Type the profile name to confirm, or pass --yes.
setlocal
if "%MINIKUBE_PROFILE%"=="" set MINIKUBE_PROFILE=hugo
if "%1"=="--yes" goto run
set /p ANSWER=This deletes minikube profile %MINIKUBE_PROFILE% with everything in it. Type %MINIKUBE_PROFILE% to continue:
if not "%ANSWER%"=="%MINIKUBE_PROFILE%" (
  echo [hugo minikube] aborted
  exit /b 1
)
:run
minikube delete -p %MINIKUBE_PROFILE%
endlocal
