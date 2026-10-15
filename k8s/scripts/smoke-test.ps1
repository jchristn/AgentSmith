# Smoke test of a deployed Hugo for Windows (smoke-test.bat runs it). The same checks as smoke-test.sh: pods Ready and
# one server replica, health on the server and through the dashboard, the console, sign-in with the forced password
# change, the connector catalog, the compiled instructions of profile Hugo, and with observability the Grafana Hugo
# folder and the Prometheus scrape. Environment: HUGO_KUBE_CONTEXT, HUGO_SMOKE_EMAIL, HUGO_SMOKE_PASSWORD,
# HUGO_SMOKE_NEW_PASSWORD (see smoke-test.sh).
$ErrorActionPreference = 'Stop'
$ctx = @()
if ($env:HUGO_KUBE_CONTEXT) { $ctx = @('--context', $env:HUGO_KUBE_CONTEXT) }
$email = if ($env:HUGO_SMOKE_EMAIL) { $env:HUGO_SMOKE_EMAIL } else { 'anderson@hugo.local' }
$password = if ($env:HUGO_SMOKE_PASSWORD) { $env:HUGO_SMOKE_PASSWORD } else { 'password' }
$passed = 0
$jobs = @()

function Pass($m) { $script:passed++; Write-Host "[hugo] PASS $m" }
function Die($m) { Write-Host "[hugo] FAIL $m"; $script:jobs | ForEach-Object { Stop-Process -Id $_.Id -ErrorAction SilentlyContinue }; exit 1 }
function Kc { param([string[]]$a) & kubectl @ctx -n hugo @a }
function Forward($svc, $ports) {
  $p = Start-Process -FilePath kubectl -ArgumentList ($ctx + @('-n', 'hugo', 'port-forward', '--address', '127.0.0.1', $svc, $ports)) -PassThru -WindowStyle Hidden
  $script:jobs += $p
  $local = $ports.Split(':')[0]
  for ($i = 0; $i -lt 40; $i++) {
    try { Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$local/" -TimeoutSec 2 | Out-Null; return } catch {
      if ($_.Exception.Response) { return }
      Start-Sleep -Milliseconds 500
    }
  }
  Die "port-forward to 127.0.0.1:$local did not come up"
}

Kc @('wait', '--for=condition=Ready', 'pod', '-l', 'app.kubernetes.io/name=hugo-server', '--timeout=120s') | Out-Null
if ($LASTEXITCODE -ne 0) { Die 'the server pod is not Ready' }
Kc @('wait', '--for=condition=Ready', 'pod', '-l', 'app.kubernetes.io/name=hugo-dashboard', '--timeout=120s') | Out-Null
if ($LASTEXITCODE -ne 0) { Die 'the dashboard pod is not Ready' }
$replicas = Kc @('get', 'deployment', 'hugo-server', '-o', 'jsonpath={.spec.replicas}')
$strategy = Kc @('get', 'deployment', 'hugo-server', '-o', 'jsonpath={.spec.strategy.type}')
if ($replicas -ne '1' -or $strategy -ne 'Recreate') { Die "the server must run 1 replica with Recreate (found $replicas, $strategy)" }
Pass 'pods Ready; server runs 1 replica with the Recreate strategy'

Forward 'svc/hugo-server' '18800:8800'
Forward 'svc/hugo-dashboard' '18801:80'
$api = 'http://127.0.0.1:18800'
$ui = 'http://127.0.0.1:18801'

try { Invoke-RestMethod "$api/v1.0/api/health" | Out-Null } catch { Die 'GET /v1.0/api/health on the server' }
Pass 'server health'
try { Invoke-RestMethod "$ui/v1.0/api/health" | Out-Null } catch { Die 'GET /v1.0/api/health through the dashboard proxy' }
Pass 'server health through the dashboard proxy'
$index = (Invoke-WebRequest -UseBasicParsing "$ui/").Content
if ($index -notmatch 'id="root"') { Die 'the dashboard did not serve the console' }
Pass 'dashboard serves the console'

function Login($pw) {
  $body = @{ tenantId = 'default'; email = $email; password = $pw } | ConvertTo-Json
  (Invoke-RestMethod -Method Post -Uri "$api/v1.0/token" -ContentType 'application/json' -Body $body).token
}
try { $token = Login $password } catch { Die "sign-in as $email (set HUGO_SMOKE_PASSWORD to the current password)" }
Pass "signed in as $email"
$headers = @{ Authorization = "Bearer $token" }
$code = 200
try { Invoke-WebRequest -UseBasicParsing -Headers $headers "$api/v1.0/api/connectors" | Out-Null } catch { $code = [int]$_.Exception.Response.StatusCode }
if ($code -eq 403) {
  $new = $env:HUGO_SMOKE_NEW_PASSWORD
  if (-not $new) { $new = 'Hugo-' + [Guid]::NewGuid().ToString('N').Substring(0, 16) }
  $body = @{ currentPassword = $password; newPassword = $new } | ConvertTo-Json
  try { Invoke-RestMethod -Method Post -Uri "$api/v1.0/api/users/me/password" -Headers $headers -ContentType 'application/json' -Body $body | Out-Null } catch { Die 'password change' }
  $token = Login $new
  $headers = @{ Authorization = "Bearer $token" }
  Pass 'password change was required (403) and is done'
  if (-not $env:HUGO_SMOKE_NEW_PASSWORD) { Write-Host "[hugo]      $email now signs in with the generated password (shown once): $new" }
} elseif ($code -eq 200) {
  Pass 'password already changed earlier'
} else { Die "unexpected status $code from /v1.0/api/connectors" }

$connectors = Invoke-RestMethod -Headers $headers "$api/v1.0/api/connectors"
if (@($connectors).Count -lt 1) { Die 'the connector catalog is empty' }
Pass "connector catalog lists $(@($connectors).Count) connectors"

$profiles = Invoke-RestMethod -Headers $headers "$api/v1.0/api/agent-profiles"
$hugo = $profiles.objects | Where-Object { $_.name -eq 'Hugo' } | Select-Object -First 1
if (-not $hugo) { Die 'agent profile Hugo not found' }
$compiled = (Invoke-WebRequest -UseBasicParsing -Headers $headers "$api/v1.0/api/agent-profiles/$($hugo.id)/compiled-instructions").Content
if ($compiled -notmatch 'Agent Smith') { Die 'the compiled instructions of profile Hugo do not mention Agent Smith' }
Pass "compiled instructions of profile Hugo introduce Agent Smith"

Kc @('get', 'service', 'hugo-grafana') 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
  Forward 'svc/hugo-grafana' '13000:3000'
  $b64 = Kc @('get', 'secret', 'hugo-secrets', '-o', 'jsonpath={.data.grafana-admin-password}')
  $gf = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
  $auth = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("admin:$gf"))
  $found = Invoke-RestMethod -Headers @{ Authorization = $auth } 'http://127.0.0.1:13000/api/search?folderUIDs=hugo&type=dash-db'
  if (@($found).Count -lt 1) { Die 'Grafana folder Hugo is empty' }
  Pass "Grafana folder Hugo holds $(@($found).Count) dashboards"
}

$jobs | ForEach-Object { Stop-Process -Id $_.Id -ErrorAction SilentlyContinue }
Write-Host "[hugo] smoke test passed ($passed checks)"
exit 0
