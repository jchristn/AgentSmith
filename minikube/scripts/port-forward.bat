@echo off
rem Forwards Hugo in minikube to 127.0.0.1: dashboard 8801, REST, MCP, and A2A 8800, Grafana 3000 with observability.
setlocal
if "%MINIKUBE_PROFILE%"=="" set MINIKUBE_PROFILE=hugo
set HUGO_KUBE_CONTEXT=%MINIKUBE_PROFILE%
call "%~dp0..\..\k8s\scripts\port-forward.bat"
endlocal
