@echo off
REM Create docker\.env from .env.example on first run, generate missing keys, and start the stack.
cd /d "%~dp0"
if not exist .env copy .env.example .env >nul
powershell -NoProfile -Command "$p='.env'; $c=Get-Content $p; foreach ($k in 'HUGO_MASTER_KEY','HUGO_AUTH_SIGNING_KEY') { if (-not ($c -match ('^'+$k+'=.+'))) { $b=New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $v=[Convert]::ToBase64String($b); if ($c -match ('^'+$k+'=')) { $c=$c -replace ('^'+$k+'=.*'),($k+'='+$v) } else { $c+=($k+'='+$v) }; Write-Host ('Generated '+$k+' in docker\.env. Back it up.') } }; Set-Content $p $c"
docker compose up -d %*
docker ps -a --filter "label=com.docker.compose.project=hugo"
