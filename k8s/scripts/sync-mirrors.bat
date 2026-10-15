@echo off
rem Refreshes the copies Kustomize and Helm need from their sources. See sync-mirrors.sh.
setlocal
set K8S=%~dp0..
set REPO=%~dp0..\..
del /q "%K8S%\components\observability\dashboards\*.json" "%K8S%\helm\hugo\files\observability\dashboards\*.json" 2>nul
copy /y "%REPO%\assets\grafana\*.json" "%K8S%\components\observability\dashboards\" >nul
copy /y "%REPO%\assets\grafana\*.json" "%K8S%\helm\hugo\files\observability\dashboards\" >nul
for %%f in (loki.yaml tempo.yaml grafana.ini grafana-dashboards.yaml grafana-datasources.yaml) do copy /y "%REPO%\docker\observability\%%f" "%K8S%\components\observability\config\%%f" >nul
copy /y "%K8S%\components\observability\config\*" "%K8S%\helm\hugo\files\observability\" >nul
copy /y "%K8S%\base\config\hugo.json" "%K8S%\helm\hugo\files\hugo.json" >nul
echo [hugo] mirrors refreshed. If dashboards were added or removed, update k8s\components\observability\kustomization.yaml and grafana.yaml.
endlocal
