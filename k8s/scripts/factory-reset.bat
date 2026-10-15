@echo off
rem DESTRUCTIVE: deletes namespace hugo (every volume and Secret hugo-secrets), creates new secrets, and redeploys
rem the same shape. Requires typing RESET. An external database is not touched.
rem Usage: factory-reset.bat [k3s^|production^|minikube] [--with postgres,observability]
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
echo This deletes namespace hugo: ALL Hugo data, volumes, and Secret hugo-secrets ^(the master key^).
echo It then redeploys %TARGET% with "%WITH%" and new secrets.
set /p ANSWER=Type RESET to continue:
if not "%ANSWER%"=="RESET" (
  echo [hugo] aborted
  exit /b 1
)
kubectl %KCTX% delete namespace hugo --wait=true --ignore-not-found
call "%~dp0generate-secrets.bat" || exit /b 1
call "%~dp0render.bat" %TARGET% "%WITH%" || exit /b 1
call "%~dp0wait-rollout.bat"
endlocal
