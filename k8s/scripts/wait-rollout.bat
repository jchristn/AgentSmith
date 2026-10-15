@echo off
rem Waits for PostgreSQL (when deployed), the server, the dashboard, and the observability stack (when deployed).
setlocal
set KCTX=
if not "%HUGO_KUBE_CONTEXT%"=="" set KCTX=--context %HUGO_KUBE_CONTEXT%
echo [hugo] waiting for rollout
kubectl %KCTX% -n hugo get statefulset hugo-postgres >nul 2>&1 && (
  kubectl %KCTX% -n hugo rollout status statefulset/hugo-postgres --timeout=300s || exit /b 1
)
kubectl %KCTX% -n hugo rollout status deployment/hugo-server --timeout=600s || exit /b 1
kubectl %KCTX% -n hugo rollout status deployment/hugo-dashboard --timeout=300s || exit /b 1
kubectl %KCTX% -n hugo get deployment hugo-grafana >nul 2>&1 && (
  for %%d in (hugo-prometheus hugo-loki hugo-tempo hugo-alloy hugo-grafana) do kubectl %KCTX% -n hugo rollout status deployment/%%d --timeout=600s
)
kubectl %KCTX% -n hugo get pods -o wide
endlocal
