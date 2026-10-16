@echo off
REM Create docker\.env from .env.example on first run, generate missing keys, and start the stack.
cd /d "%~dp0"
if not exist .env copy .env.example .env >nul
if errorlevel 1 exit /b 1
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $p='.env'; $c=@(Get-Content $p); foreach ($k in 'HUGO_MASTER_KEY','HUGO_AUTH_SIGNING_KEY') { $line=@($c | Where-Object { $_ -match ('^'+$k+'=') } | Select-Object -Last 1); if ($line.Count -eq 0 -or [string]::IsNullOrWhiteSpace(($line[0] -replace ('^'+$k+'='),'').Trim().Trim([char]34).Trim([char]39))) { $b=New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $v=[Convert]::ToBase64String($b); if ($c -match ('^'+$k+'=')) { $c=$c -replace ('^'+$k+'=.*'),($k+'='+$v) } else { $c+=($k+'='+$v) }; Write-Host ('Generated '+$k+' in docker\.env. Back it up.') } }; Set-Content $p $c"
if errorlevel 1 exit /b 1
docker compose up -d %*
if errorlevel 1 exit /b 1
docker ps -a --filter "label=com.docker.compose.project=hugo"
