@echo off
rem Creates Secret hugo-secrets in namespace hugo with random values (see generate-secrets.sh for the keys):
rem master-key, signing-key, db-password, grafana-admin-password. Set HUGO_GRAFANA_PASSWORD to choose the Grafana
rem password. An existing Secret is kept unless --force is given, because a new master key cannot decrypt the
rem credentials stored with the old one. HUGO_KUBE_CONTEXT selects the kubectl context.
setlocal enabledelayedexpansion
set KCTX=
if not "%HUGO_KUBE_CONTEXT%"=="" set KCTX=--context %HUGO_KUBE_CONTEXT%
set FORCE=0
if "%1"=="--force" set FORCE=1

kubectl %KCTX% get namespace hugo >nul 2>&1 || (kubectl %KCTX% create namespace hugo --dry-run=client -o yaml | kubectl %KCTX% apply -f - >nul)

set RAND=powershell -NoProfile -Command "$b=New-Object byte[] 64; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); ([Convert]::ToBase64String($b) -replace '[^A-Za-z0-9]','').Substring(0,
set KEY32=powershell -NoProfile -Command "$b=New-Object byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)"

set CREATE=1
kubectl %KCTX% -n hugo get secret hugo-secrets >nul 2>&1 && set CREATE=0
if "%FORCE%"=="1" set CREATE=1
if "%CREATE%"=="0" (
  echo [hugo] secret hugo-secrets exists; keeping it ^(use --force to rotate, which makes stored credentials unreadable^)
  goto :eof
)
for /f %%v in ('%KEY32%') do set MASTER_KEY=%%v
for /f %%v in ('%RAND%48)"') do set SIGNING_KEY=%%v
for /f %%v in ('%RAND%32)"') do set DB_PASSWORD=%%v
set GRAFANA_PASSWORD=%HUGO_GRAFANA_PASSWORD%
if "!GRAFANA_PASSWORD!"=="" for /f %%v in ('%RAND%24)"') do set GRAFANA_PASSWORD=%%v
kubectl %KCTX% -n hugo create secret generic hugo-secrets --from-literal=master-key=!MASTER_KEY! --from-literal=signing-key=!SIGNING_KEY! --from-literal=db-password=!DB_PASSWORD! --from-literal=grafana-admin-password=!GRAFANA_PASSWORD! --dry-run=client -o yaml | kubectl %KCTX% -n hugo apply -f - >nul || exit /b 1
echo [hugo] created secret hugo-secrets in namespace hugo
if "%HUGO_GRAFANA_PASSWORD%"=="" echo [hugo]   Grafana user admin, password ^(generated, shown once^): !GRAFANA_PASSWORD!
echo [hugo]   back it up now, outside the cluster and the repository; without master-key the stored credentials are lost:
echo [hugo]   kubectl -n hugo get secret hugo-secrets -o yaml ^> hugo-secrets.backup.yaml
endlocal
