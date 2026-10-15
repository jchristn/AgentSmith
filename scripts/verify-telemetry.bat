@echo off
REM Telemetry verifier (plan task P10.20), Windows version of verify-telemetry.sh with the same options and exit
REM codes. Drives a small workload through the Hugo REST API, waits for Prometheus to scrape the server, then checks
REM that every expected metric family exists in Prometheus. Run "scripts\verify-telemetry.bat --help" for options.
REM The PowerShell below the marker line does the work (Windows PowerShell 5.1 or PowerShell 7).
setlocal
set "VT_SELF=%~f0"
set "VT_ARGS=%*"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s = Get-Content -Raw -LiteralPath $env:VT_SELF; $i = $s.IndexOf('#POWERSHELL' + '_BEGIN'); Invoke-Expression $s.Substring($i)"
exit /b %ERRORLEVEL%

#POWERSHELL_BEGIN
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function EnvOr([string] $name, [string] $fallback) {
    $v = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrEmpty($v)) { return $fallback } else { return $v }
}

$ScriptDir = Split-Path -Parent $env:VT_SELF
$BaseUrl = EnvOr 'HUGO_BASE_URL' 'http://127.0.0.1:8800'
$PromUrl = EnvOr 'HUGO_PROMETHEUS_URL' 'http://127.0.0.1:9090'
$Job = EnvOr 'HUGO_PROMETHEUS_JOB' 'hugo-server'
$Tenant = EnvOr 'HUGO_TENANT' 'default'
$Email = EnvOr 'HUGO_EMAIL' 'anderson@hugo.local'
$Password = EnvOr 'HUGO_PASSWORD' 'password'
$NewPassword = EnvOr 'HUGO_NEW_PASSWORD' ''
$Iterations = 3
$ScrapeTimeout = 60
$Catalog = Join-Path $ScriptDir '..\docs\TELEMETRY.md'

$Help = @'
Usage: scripts\verify-telemetry.bat [options]

  --base-url URL         Hugo REST base URL [HUGO_BASE_URL] (default http://127.0.0.1:8800)
  --prometheus-url URL   Prometheus base URL [HUGO_PROMETHEUS_URL] (default http://127.0.0.1:9090)
  --job NAME             Prometheus scrape job of the server [HUGO_PROMETHEUS_JOB] (default hugo-server)
  --tenant ID            Tenant to sign in to [HUGO_TENANT] (default default)
  --email EMAIL          User to sign in as [HUGO_EMAIL] (default anderson@hugo.local)
  --password PASSWORD    Current password [HUGO_PASSWORD] (default password, the seeded value)
  --new-password PASS    When the server answers mustChangePassword, change the password to PASS through
                         POST /v1.0/api/users/me/password and sign in again [HUGO_NEW_PASSWORD]. PASS is also
                         tried when the current password is rejected, so a second run works unchanged.
  --iterations N         Times to repeat the authenticated reads (default 3)
  --scrape-timeout S     Seconds to wait for a Prometheus scrape after the workload (default 60)
  --catalog FILE         Catalog file to read hugo_* families from (default docs\TELEMETRY.md)
  -h, --help             Show this help

Exit code 0 when every required family exists, 1 when one is missing, 2 on a usage or connectivity error.
See scripts/verify-telemetry.sh for how families are classified (required, conditional, unemitted, pending).
'@

$tokens = @()
if ($env:VT_ARGS) {
    foreach ($m in [regex]::Matches($env:VT_ARGS, '"([^"]*)"|(\S+)')) {
        if ($m.Groups[1].Success) { $tokens += $m.Groups[1].Value } else { $tokens += $m.Groups[2].Value }
    }
}
for ($a = 0; $a -lt $tokens.Count; $a++) {
    $opt = $tokens[$a]
    if ($opt -eq '-h' -or $opt -eq '--help') { Write-Output $Help; exit 0 }
    if ($a + 1 -ge $tokens.Count) { Write-Output "Option $opt needs a value (see --help)"; exit 2 }
    $val = $tokens[++$a]
    switch ($opt) {
        '--base-url' { $BaseUrl = $val }
        '--prometheus-url' { $PromUrl = $val }
        '--job' { $Job = $val }
        '--tenant' { $Tenant = $val }
        '--email' { $Email = $val }
        '--password' { $Password = $val }
        '--new-password' { $NewPassword = $val }
        '--iterations' { $Iterations = [int] $val }
        '--scrape-timeout' { $ScrapeTimeout = [int] $val }
        '--catalog' { $Catalog = $val }
        default { Write-Output "Unknown option: $opt (see --help)"; exit 2 }
    }
}
$BaseUrl = $BaseUrl.TrimEnd('/')
$PromUrl = $PromUrl.TrimEnd('/')

# Keep these lists identical to verify-telemetry.sh.
$RuntimeRequired = @('http_server_request_duration_seconds', 'http_server_active_requests', 'watson_server_up',
    'watson_server_connections_active', 'process_uptime_seconds', 'process_memory_usage_bytes',
    'dotnet_process_cpu_time_seconds_total')
$Conditional = @('hugo_auth_rate_limited_total', 'hugo_db_errors_total', 'hugo_db_transaction_retries_total',
    'hugo_retention_deleted_total', 'hugo_background_duration_seconds', 'hugo_background_iterations_total',
    'hugo_background_jobs_total', 'hugo_background_job_duration_seconds', 'hugo_outbound_requests_total',
    'hugo_outbound_duration_seconds', 'hugo_observability_reachable', 'hugo_telemetry_labels_folded_total',
    'hugo_telemetry_diagnostics_total', 'hugo_secret_operations_total', 'hugo_secret_redactions_total',
    'hugo_sse_streams_active', 'hugo_sse_events_total', 'hugo_audit_events_total', 'hugo_authz_denials_total',
    'hugo_agent_turns_total', 'hugo_agent_turn_duration_seconds', 'hugo_agent_stage_duration_seconds',
    'hugo_agent_iterations', 'hugo_model_requests_total', 'hugo_model_request_duration_seconds',
    'hugo_model_time_to_first_token_seconds', 'hugo_model_tokens_total', 'hugo_tool_invocations_total',
    'hugo_tool_duration_seconds', 'hugo_tool_output_bytes', 'hugo_connector_sessions_active', 'hugo_approvals_total',
    'hugo_approval_wait_seconds', 'hugo_mcp_requests_total', 'hugo_a2a_requests_total',
    'hugo_instructions_compile_duration_seconds', 'hugo_resource_health')
$KnownUnemitted = @('hugo_authz_requests_total')
$Section21 = @('hugo_tool_invocations_total', 'hugo_tool_duration_seconds', 'hugo_tool_output_bytes',
    'hugo_connector_sessions_active', 'hugo_approvals_total', 'hugo_approval_wait_seconds', 'hugo_agent_turns_total',
    'hugo_agent_turn_duration_seconds', 'hugo_agent_stage_duration_seconds', 'hugo_agent_iterations',
    'hugo_model_requests_total', 'hugo_model_request_duration_seconds', 'hugo_model_time_to_first_token_seconds',
    'hugo_model_tokens_total', 'hugo_mcp_requests_total', 'hugo_a2a_requests_total',
    'hugo_instructions_compile_duration_seconds', 'hugo_secret_operations_total', 'hugo_secret_redactions_total',
    'hugo_auth_requests_total', 'hugo_authz_requests_total', 'hugo_background_jobs_total',
    'hugo_background_job_duration_seconds', 'hugo_resource_health')

$script:Warnings = 0
function Warn([string] $msg) { Write-Output "WARN $msg"; $script:Warnings++ }

# Call <method> <path> [token] [body object]: returns @{ Status; Body }.
function Call([string] $method, [string] $path, [string] $token = '', $body = $null) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $params = @{ Uri = "$BaseUrl$path"; Method = $method; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 15 }
    if ($null -ne $body) { $params['Body'] = ($body | ConvertTo-Json -Compress); $params['ContentType'] = 'application/json' }
    try {
        $r = Invoke-WebRequest @params
        return @{ Status = [int] $r.StatusCode; Body = [string] $r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($null -ne $resp) { return @{ Status = [int] $resp.StatusCode; Body = '' } }
        return @{ Status = 0; Body = '' }
    }
}

function IssueToken([string] $pw) {
    $r = Call 'POST' '/v1.0/token' '' @{ tenantId = $Tenant; email = $Email; password = $pw }
    if ($r.Status -ne 200) { return $null }
    return ($r.Body | ConvertFrom-Json)
}

Write-Output "Hugo:       $BaseUrl"
Write-Output "Prometheus: $PromUrl (job $Job)"

if ((Call 'GET' '/v1.0/api/health').Status -ne 200) {
    Write-Output "verify-telemetry: $BaseUrl/v1.0/api/health is not answering 200; is the server running?"
    exit 2
}
try { Invoke-WebRequest -Uri "$PromUrl/-/ready" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch {
    Write-Output "verify-telemetry: Prometheus at $PromUrl is not ready"
    exit 2
}

Write-Output 'Workload: anonymous routes'
foreach ($p in @('/v1.0/api/health', '/v1.0/api/health/live', '/v1.0/api/health/ready', '/v1.0/api/info')) {
    Write-Output ("  GET $p -> " + (Call 'GET' $p).Status)
}

Write-Output 'Workload: rejected requests (authentication failures)'
Write-Output ('  POST /v1.0/token (wrong password) -> ' + (Call 'POST' '/v1.0/token' '' @{ tenantId = $Tenant; email = $Email; password = 'verify-telemetry-wrong-password' }).Status)
Write-Output ('  GET /v1.0/api/users/me (no token) -> ' + (Call 'GET' '/v1.0/api/users/me').Status)
Write-Output ('  GET /v1.0/api/users/me (bad token) -> ' + (Call 'GET' '/v1.0/api/users/me' 'verify-telemetry-invalid-token').Status)

Write-Output "Workload: sign in as $Email (tenant $Tenant)"
$session = IssueToken $Password
if ($session) {
    Write-Output "  POST /v1.0/token -> 200 (mustChangePassword=$($session.mustChangePassword))"
} elseif ($NewPassword -and ($session = IssueToken $NewPassword)) {
    $Password = $NewPassword
    Write-Output "  POST /v1.0/token with the new password -> 200 (mustChangePassword=$($session.mustChangePassword))"
} else {
    Warn "sign-in failed for $Email; authenticated reads skipped"
}

$Token = ''
if ($session) { $Token = $session.token }
if ($session -and $session.mustChangePassword) {
    if ($NewPassword -and $NewPassword -ne $Password) {
        $r = Call 'POST' '/v1.0/api/users/me/password' $Token @{ currentPassword = $Password; newPassword = $NewPassword }
        Write-Output "  POST /v1.0/api/users/me/password -> $($r.Status)"
        $again = $null
        if ($r.Status -eq 200) { $again = IssueToken $NewPassword }
        if ($again) {
            $Password = $NewPassword
            $session = $again
            $Token = $again.token
            Write-Output "  POST /v1.0/token with the new password -> 200 (mustChangePassword=$($again.mustChangePassword))"
        } else {
            Warn "password change failed ($($r.Status))"
        }
    }
    if ($session.mustChangePassword) {
        Write-Output ('  GET /v1.0/api/users/me (password change pending) -> ' + (Call 'GET' '/v1.0/api/users/me' $Token).Status)
        Warn "$Email must change the password; pass --new-password (or HUGO_NEW_PASSWORD) to run the authenticated reads"
        $Token = ''
    }
}

if ($Token) {
    Write-Output "Workload: authenticated reads x$Iterations (403 is expected for system administrator routes)"
    $paths = @('/v1.0/api/users/me', '/v1.0/token/details', '/v1.0/api/server-info', '/v1.0/api/observability/status',
        '/v1.0/api/request-history?maxResults=5', '/v1.0/api/request-history/summary', '/v1.0/api/settings',
        '/v1.0/api/tenants', '/v1.0/api/system/master-key')
    for ($n = 0; $n -lt $Iterations; $n++) {
        foreach ($p in $paths) { Write-Output ("  GET $p -> " + (Call 'GET' $p $Token).Status) }
    }
    # A short-lived server-sent events stream (Invoke-WebRequest would wait for the stream to end).
    try {
        Add-Type -AssemblyName System.Net.Http
        $hc = New-Object System.Net.Http.HttpClient
        $hc.Timeout = [TimeSpan]::FromSeconds(10)
        $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$BaseUrl/v1.0/api/events")
        $req.Headers.Add('Authorization', "Bearer $Token")
        $req.Headers.Add('Accept', 'text/event-stream')
        $resp = $hc.SendAsync($req, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        Start-Sleep -Seconds 3
        $resp.Dispose()
        $hc.Dispose()
    } catch { }
    Write-Output '  GET /v1.0/api/events (3 second stream) -> closed'
    Write-Output ('  POST /v1.0/token/refresh -> ' + (Call 'POST' '/v1.0/token/refresh' $Token).Status)
}

$WorkloadEnd = [DateTimeOffset]::UtcNow

Write-Output "Waiting for Prometheus to scrape $Job after the workload (up to ${ScrapeTimeout}s)"
$deadline = (Get-Date).AddSeconds($ScrapeTimeout)
$scraped = $false
while (-not $scraped -and (Get-Date) -lt $deadline) {
    try {
        $targets = (Invoke-WebRequest -Uri "$PromUrl/api/v1/targets?state=active" -UseBasicParsing -TimeoutSec 10).Content | ConvertFrom-Json
        foreach ($t in $targets.data.activeTargets) {
            if ($t.labels.job -eq $Job -and $t.health -eq 'up') {
                $raw = [string] $t.lastScrape
                $raw = [regex]::Replace($raw, '\.(\d{1,7})\d*', '.$1')
                $when = [DateTimeOffset]::Parse($raw, [Globalization.CultureInfo]::InvariantCulture)
                if ($when -gt $WorkloadEnd) { $scraped = $true }
            }
        }
    } catch { }
    if (-not $scraped) { Start-Sleep -Seconds 2 }
}
if (-not $scraped) {
    Write-Output "verify-telemetry: Prometheus did not scrape job $Job within ${ScrapeTimeout}s (is the target up?)"
    exit 1
}
Write-Output '  scraped'

if (-not (Test-Path -LiteralPath $Catalog)) { Write-Output "verify-telemetry: catalog $Catalog not found (use --catalog)"; exit 2 }
$catalogNames = @()
foreach ($line in Get-Content -LiteralPath $Catalog) {
    $m = [regex]::Match($line, '^\| `(hugo_[a-z0-9_]+)` \|')
    if ($m.Success -and $catalogNames -notcontains $m.Groups[1].Value) { $catalogNames += $m.Groups[1].Value }
}
if ($catalogNames.Count -eq 0) { Write-Output "verify-telemetry: no hugo_* families found in $Catalog"; exit 2 }

try {
    $match = [uri]::EscapeDataString("{job=`"$Job`"}")
    $names = ((Invoke-WebRequest -Uri "$PromUrl/api/v1/label/__name__/values?match[]=$match" -UseBasicParsing -TimeoutSec 15).Content | ConvertFrom-Json).data
} catch { Write-Output 'FAIL could not read metric names from Prometheus'; exit 2 }
$nameSet = @{}
foreach ($n in $names) { $nameSet[$n] = $true }
function Present([string] $f) { return ($nameSet.ContainsKey($f) -or $nameSet.ContainsKey("${f}_bucket") -or $nameSet.ContainsKey("${f}_count")) }

$required = @($catalogNames | Where-Object { $Conditional -notcontains $_ -and $KnownUnemitted -notcontains $_ }) + $RuntimeRequired
$cond = @($catalogNames | Where-Object { $Conditional -contains $_ })
$gaps = @($catalogNames | Where-Object { $KnownUnemitted -contains $_ -and $Conditional -notcontains $_ })
$pending = @($Section21 | Where-Object { $catalogNames -notcontains $_ })

$missing = @()
$absent = 0
Write-Output ''
Write-Output "Required families ($($required.Count)):"
foreach ($f in $required) {
    if (Present $f) { Write-Output "  ok   $f" } else { Write-Output "  MISS $f"; $missing += $f }
}
Write-Output ''
Write-Output "Conditional catalog families ($($cond.Count), warning only):"
foreach ($f in $cond) { if (Present $f) { Write-Output "  ok   $f" } else { Write-Output "  warn $f"; $absent++ } }
Write-Output ''
Write-Output "Catalog families nothing records yet ($($gaps.Count), warning only):"
foreach ($f in $gaps) { if (Present $f) { Write-Output "  ok   $f  (now emitted: remove it from KnownUnemitted)" } else { Write-Output "  warn $f"; $absent++ } }
Write-Output ''
Write-Output "Section 21 families not in the catalog ($($pending.Count), warning only):"
foreach ($f in $pending) { if (Present $f) { Write-Output "  ok   $f" } else { Write-Output "  warn $f"; $absent++ } }
Write-Output ''
if ($script:Warnings -gt 0) { Write-Output "$($script:Warnings) workload warning(s), see WARN lines above" }
if ($missing.Count -gt 0) {
    Write-Output "FAIL $($missing.Count) required metric families missing: $($missing -join ', ')"
    exit 1
}
Write-Output "PASS $($required.Count) required families present ($absent conditional or pending families absent, see warn lines)"
exit 0
