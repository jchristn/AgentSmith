@echo off
rem Builds and pushes the multi-arch (linux/amd64, linux/arm64) jchristn77/hugo-server image with Docker Build Cloud,
rem tagged with the supplied tag and latest, then pulls both tags back locally.
rem Usage: build-server.bat ^<tag^>   (for example v0.1.0)
rem Overrides: BUILDER (default cloud-jchristn77-jchristn77), PLATFORMS (default linux/amd64,linux/arm64/v8).
setlocal
if "%~1"=="" (
  echo usage: %~nx0 ^<tag^>
  exit /b 2
)
set TAG=%~1
set IMAGE_NAME=jchristn77/hugo-server
set DOCKERFILE_PATH=src/Hugo.Server/Dockerfile
set BUILD_CONTEXT=.
if "%BUILDER%"=="" set BUILDER=cloud-jchristn77-jchristn77
if "%PLATFORMS%"=="" set PLATFORMS=linux/amd64,linux/arm64/v8
pushd "%~dp0"
docker buildx build --builder %BUILDER% --platform %PLATFORMS% -f %DOCKERFILE_PATH% -t %IMAGE_NAME%:%TAG% -t %IMAGE_NAME%:latest --push %BUILD_CONTEXT% || (popd & exit /b 1)
docker pull %IMAGE_NAME%:%TAG% || (popd & exit /b 1)
docker pull %IMAGE_NAME%:latest || (popd & exit /b 1)
popd
echo built and pushed %IMAGE_NAME%:%TAG% and %IMAGE_NAME%:latest
endlocal
