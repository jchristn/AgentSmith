@echo off
rem Deploys Hugo to the current kubectl context (or HUGO_KUBE_CONTEXT). See deploy.sh.
rem Usage: deploy.bat [k3s^|production^|minikube] [--with postgres,observability]
rem To use local images on k3s (--import in deploy.sh), run import-images.bat first with K3D_CLUSTER or K3S_CONTAINER
rem set; otherwise the cluster pulls from Docker Hub.
setlocal
set TARGET=k3s
set WITH=
:args
if "%~1"=="" goto run
if "%~1"=="--with" (
  set WITH=%~2
  shift
  shift
  goto args
)
set TARGET=%~1
shift
goto args
:run
call "%~dp0generate-secrets.bat" || exit /b 1
call "%~dp0render.bat" %TARGET% "%WITH%" || exit /b 1
call "%~dp0wait-rollout.bat" || exit /b 1
echo [hugo] reach it with k8s\scripts\port-forward.bat: dashboard http://127.0.0.1:8801, REST, MCP, and A2A http://127.0.0.1:8800
echo [hugo] sign in to tenant default as anderson@hugo.local or admin@hugo.local with password 'password' and change it
endlocal
