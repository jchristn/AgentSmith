@echo off
rem Copies jchristn77/hugo-server and hugo-dashboard at HUGO_TAG from the local Docker daemon into k3s: the k3d cluster
rem named by K3D_CLUSTER, or the rancher/k3s container named by K3S_CONTAINER. --pull runs docker pull first.
setlocal
if "%HUGO_TAG%"=="" set HUGO_TAG=v0.1.0
for %%i in (jchristn77/hugo-server jchristn77/hugo-dashboard) do (
  if "%1"=="--pull" docker pull %%i:%HUGO_TAG% || exit /b 1
  rem Only this machine's platform is saved; k3d image import fails on multi-platform images (see import-images.sh).
  if not "%K3D_CLUSTER%"=="" (
    for %%r in (server agent) do (
      for /f %%n in ('docker ps -q --filter "label=k3d.cluster=%K3D_CLUSTER%" --filter "label=k3d.role=%%r"') do (
        docker save --platform linux/%PROCESSOR_ARCHITECTURE:AMD64=amd64% %%i:%HUGO_TAG% | docker exec -i %%n ctr -n k8s.io images import - || exit /b 1
      )
    )
  ) else if not "%K3S_CONTAINER%"=="" (
    docker save --platform linux/%PROCESSOR_ARCHITECTURE:AMD64=amd64% %%i:%HUGO_TAG% | docker exec -i %K3S_CONTAINER% ctr -n k8s.io images import - || exit /b 1
  ) else (
    echo [hugo] error: set K3D_CLUSTER or K3S_CONTAINER
    exit /b 1
  )
  echo [hugo] imported %%i:%HUGO_TAG%
)
endlocal
