@echo off
rem Forwards Hugo to 127.0.0.1: REST, MCP, and A2A on 8800, the dashboard on 8801, and with the observability
rem component Grafana 3000, Prometheus 9090, Loki 3100, and Tempo 3200. Each forward opens its own window; close
rem them to stop. Pick other ports first when these are taken, for example set REST_PORT=9800 and
rem set DASHBOARD_PORT=9801 (also GRAFANA_PORT, PROMETHEUS_PORT, LOKI_PORT, TEMPO_PORT).
setlocal
set KCTX=
if not "%HUGO_KUBE_CONTEXT%"=="" set KCTX=--context %HUGO_KUBE_CONTEXT%
if "%REST_PORT%"=="" set REST_PORT=8800
if "%DASHBOARD_PORT%"=="" set DASHBOARD_PORT=8801
if "%GRAFANA_PORT%"=="" set GRAFANA_PORT=3000
if "%PROMETHEUS_PORT%"=="" set PROMETHEUS_PORT=9090
if "%LOKI_PORT%"=="" set LOKI_PORT=3100
if "%TEMPO_PORT%"=="" set TEMPO_PORT=3200
start "hugo-server" kubectl %KCTX% -n hugo port-forward --address 127.0.0.1 svc/hugo-server %REST_PORT%:8800
start "hugo-dashboard" kubectl %KCTX% -n hugo port-forward --address 127.0.0.1 svc/hugo-dashboard %DASHBOARD_PORT%:80
echo [hugo] dashboard http://127.0.0.1:%DASHBOARD_PORT%   REST http://127.0.0.1:%REST_PORT%   MCP http://127.0.0.1:%REST_PORT%/mcp
kubectl %KCTX% -n hugo get service hugo-grafana >nul 2>&1 || goto :eof
start "hugo-grafana" kubectl %KCTX% -n hugo port-forward --address 127.0.0.1 svc/hugo-grafana %GRAFANA_PORT%:3000
start "hugo-prometheus" kubectl %KCTX% -n hugo port-forward --address 127.0.0.1 svc/hugo-prometheus %PROMETHEUS_PORT%:9090
start "hugo-loki" kubectl %KCTX% -n hugo port-forward --address 127.0.0.1 svc/hugo-loki %LOKI_PORT%:3100
start "hugo-tempo" kubectl %KCTX% -n hugo port-forward --address 127.0.0.1 svc/hugo-tempo %TEMPO_PORT%:3200
echo [hugo] Grafana http://127.0.0.1:%GRAFANA_PORT% ^(admin^)   Prometheus http://127.0.0.1:%PROMETHEUS_PORT%   Loki http://127.0.0.1:%LOKI_PORT%   Tempo http://127.0.0.1:%TEMPO_PORT%
endlocal
