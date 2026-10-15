@echo off
rem Non-destructive update: re-applies the overlay, restarts the server and dashboard (pulling the image again where
rem imagePullPolicy is Always), waits, and prints pod status. Volumes and Secret hugo-secrets are kept.
rem Usage: update.bat [k3s^|production^|minikube] [--with postgres,observability]
rem Without arguments, the target and components recorded by the last deploy are used.
setlocal
set KCTX=
if not "%HUGO_KUBE_CONTEXT%"=="" set KCTX=--context %HUGO_KUBE_CONTEXT%
set TARGET=
set WITH=
set WITH_SET=0
:args
if "%~1"=="" goto run
if "%~1"=="--with" (
  set WITH=%~2
  set WITH_SET=1
  shift
  shift
  goto args
)
set TARGET=%~1
shift
goto args
:run
if "%TARGET%"=="" for /f "delims=" %%t in ('kubectl %KCTX% get namespace hugo -o jsonpath^="{.metadata.annotations.hugo-deploy-target}"') do set TARGET=%%t
if "%WITH_SET%"=="0" for /f "delims=" %%c in ('kubectl %KCTX% get namespace hugo -o jsonpath^="{.metadata.annotations.hugo-deploy-components}"') do set WITH=%%c
if "%TARGET%"=="" (
  echo [hugo] error: no target given and none recorded; pass k3s, production, or minikube
  exit /b 1
)
call "%~dp0render.bat" %TARGET% "%WITH%" || exit /b 1
kubectl %KCTX% -n hugo rollout restart deployment/hugo-server deployment/hugo-dashboard || exit /b 1
call "%~dp0wait-rollout.bat"
endlocal
