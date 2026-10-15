# Hugo C# SDK

`Hugo.Sdk` is the .NET client for the Hugo REST API. It targets `net8.0` and `net10.0`, has no dependencies beyond the base class library, and is versioned with the product (`0.1.0-alpha`).

It covers:

- authentication: email and password, access key headers, access key session exchange, signed requests, the administrator API key, session refresh (manual and automatic), logout, re-authentication, password change, and tenant lookup;
- identity and administration: tenants, users, credentials, roles, permissions, role-permission maps, user role assignments, credential scope assignments, sessions, audit, settings, master key, and server information;
- the secrets vault, including parse and audited reveal;
- request history and the server-sent event stream;
- Hugo itself: connectors, resources and their knowledge, agent profiles, bindings and tool rules, compiled instructions, ask (delegate a task to a profile's own agent loop), layered prompt templates, model endpoints (validate saved or draft settings, health history, provider models), tool invocations (dry run and invoke), approvals (list, decide, await), and tenant lockdown;
- conversations: agent turns as JSON or as a stream of chat events, paged messages, export, and cancel;
- analytics: overview, tokens, tools, and models per time bucket.

Every failed call throws `HugoException`, which carries the HTTP status, the error class, the stable error code, the message, and the request ID. Everything in this SDK is verified against a live server by the test harness.

## Install

The package is not published yet. Reference the project directly:

```xml
<ProjectReference Include="path/to/sdk/csharp/src/Hugo.Sdk/Hugo.Sdk.csproj" />
```

Once published: `dotnet add package Hugo.Sdk --prerelease`.

## Quick start

```csharp
using Hugo.Sdk;
using Hugo.Sdk.Enums;
using Hugo.Sdk.Models;
using Hugo.Sdk.Requests;
using Hugo.Sdk.Responses;

using (HugoClient client = new HugoClient("http://127.0.0.1:8800"))
{
    TokenResponse login = await client.LoginAsync("anderson@hugo.local", "your-password");
    // Advisory: the account still has its seeded or reset password. Every route works either way.
    if (login.MustChangePassword) await client.ChangePasswordAsync("your-password", "a-new-password");

    // Store a secret.
    SecretRequest secretRequest = new SecretRequest();
    secretRequest.Name = "web-01-password";
    secretRequest.Kind = SecretKindEnum.Password;
    secretRequest.Values = new Dictionary<string, string> { { "password", "change-me" } };
    Secret secret = await client.Secrets.CreateAsync(secretRequest);

    // Create a resource that uses it.
    ResourceRequest resourceRequest = new ResourceRequest();
    resourceRequest.Name = "web-01";
    resourceRequest.ConnectorType = "ssh";
    resourceRequest.Environment = "prod";
    resourceRequest.Settings = new Dictionary<string, object?>
    {
        { "host", "web-01.example.com" }, { "port", 22 }, { "username", "deploy" }, { "authMethod", "Password" }
    };
    ResourceSecretBinding slot = new ResourceSecretBinding();
    slot.SlotName = "password";
    slot.SecretId = secret.Id;
    resourceRequest.SecretBindings = new List<ResourceSecretBinding> { slot };
    Resource resource = await client.Resources.CreateAsync(resourceRequest);

    // Bind it to an agent profile.
    AgentProfileRequest profileRequest = new AgentProfileRequest();
    profileRequest.Name = "ops";
    AgentProfile profile = await client.AgentProfiles.CreateAsync(profileRequest);
    BindingRequest bind = new BindingRequest();
    bind.ResourceId = resource.Id;
    bind.Alias = "web-01";
    bind.AccessMode = "ReadWriteWithApproval";
    await client.AgentProfiles.CreateBindingAsync(profile.Id, bind);

    // See what a caller receives, and what a tool call would do.
    CompiledInstructions compiled = await client.AgentProfiles.GetCompiledInstructionsAsync(profile.Id, "Mcp");
    Console.WriteLine(compiled.Text);

    ToolCallRequest call = new ToolCallRequest();
    call.ProfileId = profile.Id;
    call.Resource = "web-01";
    call.ToolName = "ssh_exec";
    call.Arguments = new { command = "uptime" };
    ToolInvocationOutcome dryRun = await client.ToolInvocations.DryRunAsync(call);
    Console.WriteLine(dryRun.Decision + " (" + dryRun.Effect + ": " + dryRun.EffectReason + ")");
}
```

The same flow is in `examples/Hugo.Sdk.Examples` (`HUGO_SDK_SERVER=http://127.0.0.1:8800 HUGO_EMAIL=... HUGO_PASSWORD=... dotnet run --project examples/Hugo.Sdk.Examples -f net10.0`).

Use `127.0.0.1`, not `localhost`, for a local server. On Windows `localhost` resolves to `::1` first and stalls before falling back, which adds latency to every call.

## Authentication

| Method | What it sends |
|---|---|
| `LoginAsync(email, password, tenantId)` | `POST /v1.0/token` with `x-email`, `x-password`, `x-tenant-id`; then `Authorization: Bearer` on every request. The tenant defaults to the server's default tenant. |
| `LoginWithAccessKeyAsync(accessKey, secretKey)` | Exchanges credential keys for a session token (credentials in `DirectHeader`, `SessionExchange`, or `Hybrid` mode). |
| `UseToken(token, tenantId, expiresUtc)` | An existing session token. |
| `UseAccessKey(accessKey, secretKey, tenantId)` | `x-access-key` and `x-secret-key` on every request. |
| `UseSignedRequests(accessKey, secretKey, tenantId)` | `x-access-key`, `x-date`, `x-nonce`, and `x-signature`: lowercase hex HMAC-SHA256 over `METHOD\nPATH\nQUERY\nTENANT\nDATE\nNONCE\nSHA256(BODY)`. The secret key never leaves the process. `RequestSigner` exposes the canonicalization. |
| `UseAdminApiKey(apiKey, tenantId)` | `x-api-key` (enabled on the server by `HUGO_ADMIN_API_KEY`). |

Session helpers: `RefreshAsync`, `LogoutAsync`, `ValidateTokenAsync`, `GetTokenDetailsAsync`, `ReauthenticateAsync(password)` (needed within a few minutes before a secret reveal), `ChangePasswordAsync(current, next)`, and `LookupTenantsAsync(email)`.

`MustChangePassword` on the token response, the token details, and the user is advisory: the account still has a seeded or administrator-set password. No route is blocked; `ChangePasswordAsync` clears the flag.

When a token from `LoginAsync` is within `Transport.RefreshSkewSeconds` (default 60) of expiry, the transport refreshes it before the next request. Set `client.Transport.AutoRefresh = false` to turn this off.

Sign-ins are rate limited (`Auth.RateLimitPerMinute`, default 10 per minute per source IP and per identity, answered with 429 `RateLimited` and `Retry-After: 60`). Set `client.Transport.RateLimitRetries` (0 to 10, default 0) to retry a 429 after waiting the `Retry-After` seconds (exponential backoff when the header is missing), capped by `client.Transport.RateLimitMaxWaitSeconds` (default 90). Streaming requests are not retried.

Credentials (access keys) get their access only from their role assignments: pass `RoleName` (for example `Viewer` or `Operator`) when creating one, or assign roles later. A credential without assignments can sign in but is denied everywhere else. `InheritOwnerBypass = true` lets a credential carry its owner's administrator or tenant administrator bypass; only a signed-in tenant or system administrator can set it (403 `BypassRequiresAdministrator` otherwise), and every change is audited as `CredentialBypassChanged`.

Identity and administration APIs are tenant-scoped (`/v1.0/api/tenants/{tenantId}/...`) and use `client.TenantId`, which login sets. Hugo's own APIs (`/v1.0/api/resources`, `/agent-profiles`, and so on) take the tenant from the credentials; a system administrator can address another tenant with `client.Transport.TenantOverride = "ten_..."`, which adds `?tenantId=` to those requests.

## Examples

Paging:

```csharp
EnumerationQuery query = new EnumerationQuery();
query.MaxResults = 100;
await foreach (User user in client.Users.EnumerateAsync(query))
{
    Console.WriteLine(user.Email);
}
```

Errors:

```csharp
try
{
    await client.Secrets.GetAsync("sec_missing");
}
catch (HugoException e) when (e.IsNotFound)
{
    Console.WriteLine(e.Code + " " + e.Message + " request " + e.RequestId);
}
```

Reveal a secret (audited, needs a recent re-authentication):

```csharp
await client.ReauthenticateAsync("your-password");
SecretRevealResponse revealed = await client.Secrets.RevealAsync(secret.Id, "rotating the deploy key");
```

Events:

```csharp
using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
{
    await foreach (SseEvent item in client.Events.StreamAsync(false, cts.Token))
    {
        Console.WriteLine(item.Event + ": " + item.Data);
    }
}
```

Approvals:

```csharp
ToolInvocationOutcome outcome = await client.ToolInvocations.InvokeAsync(call);
if (outcome.Decision == "ApprovalRequired")
{
    Approval decided = await client.Approvals.AwaitDecisionAsync(outcome.ApprovalId!, TimeSpan.FromMinutes(15));
    Console.WriteLine(decided.Status);
}
```

Layered prompt templates:

```csharp
PromptTemplateView view = await client.PromptLayers.SaveAsync("persona.agent-smith", "You are {{profile.personaName}}.", "shorter persona");
List<PromptDiffLine> diff = await client.PromptLayers.GetDiffAsync("persona.agent-smith");
await client.PromptLayers.ResetAsync("persona.agent-smith");
```

List filters (unknown enum values come back as 400 `ValidationFailed` naming the allowed values):

```csharp
ResourceFilter unhealthy = new ResourceFilter();
unhealthy.Environment = "prod";
unhealthy.Health = "Unhealthy";                       // Healthy, Unhealthy, or Unknown (last connection test)
EnumerationResult<Resource> failing = await client.Resources.ListAsync(unhealthy);

ApprovalFilter destructive = new ApprovalFilter();
destructive.Effect = "Destructive";
destructive.FromUtc = DateTime.UtcNow.AddDays(-1);
EnumerationResult<Approval> recent = await client.Approvals.ListAsync(destructive);
```

The same pattern covers `ToolInvocationFilter` (effect, fromUtc, toUtc, and more), `SecretFilter` (kind), `ModelEndpointFilter` (kind, provider, health), `SessionFilter` (principalType, userId, credentialId; each session carries `PrincipalName`), and `ConversationFilter` (agentProfileId, source).

Model endpoints:

```csharp
ModelValidationResult draft = await client.ModelEndpoints.ValidateDraftAsync(request);   // stores nothing
List<ModelEndpointHealthRecord> history = await client.ModelEndpoints.GetHealthHistoryAsync(endpoint.Id, 20);
```

Conversations (a turn needs an inference endpoint on the profile):

```csharp
ConversationRequest start = new ConversationRequest();
start.AgentProfileId = profile.Id;
Conversation conversation = await client.Conversations.CreateAsync(start);

AgentTurnResult turn = await client.Conversations.SendMessageAsync(conversation.Id, "Why is web-01 slow?");
Console.WriteLine(turn.FinishReason + ": " + turn.FinalText);

await foreach (ChatEvent item in client.Conversations.StreamMessageAsync(conversation.Id, "And web-02?"))
{
    if (item.Type == ChatEventTypes.AssistantDelta) Console.Write(item.DataAs<ChatAssistantDelta>()?.Text);
}

EnumerationResult<ConversationMessage> messages = await client.Conversations.ListMessagesAsync(conversation.Id);
```

The stream ends with `stream.closed` after `turn.completed` (or `error`).

Ask a profile (the REST twin of the MCP `ask_agent_smith` tool):

```csharp
AgentDelegationResult answer = await client.AgentProfiles.AskAsync(profile.Id, "Is checkout healthy?");
if (answer.Status == "ApprovalPending") Console.WriteLine("Waiting on approval " + answer.ApprovalId);
```

Analytics:

```csharp
AnalyticsReport report = await client.Analytics.GetOverviewAsync(DateTime.UtcNow.AddHours(-6), null, "hour");
Console.WriteLine(report.Totals.Turns + " turns, " + report.Totals.InputTokens + " input tokens");
```

MCP and A2A are separate surfaces: point an MCP client at `/mcp` and an A2A client at `/v1.0/a2a/{tenantId}/{profileName}` (see `GetInfoAsync()` for the URLs and `AgentProfiles.GetCardAsync` for a card preview).

## Test harness

`src/Test.Automated` is a console harness in the SharpAI style. It runs against a live server, provisions its own tenant and tenant administrator with the system administrator account, runs every suite (unit, auth, identity, RBAC, secrets, admin, domain, model), and deletes the tenant at the end. The model suite starts a small OpenAI-compatible stub model on a free `127.0.0.1` port, so conversations, streaming, ask, and analytics run real agent turns without an external model. The exit code is 0 when nothing failed.

```bash
cd sdk/csharp/src
HUGO_SDK_SERVER=http://127.0.0.1:8800 dotnet run --project Test.Automated -f net10.0
dotnet run --project Test.Automated -f net10.0 -- Secrets Domain   # selected suites
```

| Variable | Default |
|---|---|
| `HUGO_SDK_SERVER` | `http://127.0.0.1:8800` |
| `HUGO_SDK_ADMIN_EMAIL` | `admin@hugo.local` |
| `HUGO_SDK_ADMIN_PASSWORD` | `password` |
| `HUGO_SDK_ADMIN_TENANT` | `default` |
| `HUGO_SDK_ADMIN_API_KEY` (or `HUGO_ADMIN_API_KEY`) | unset; the API key test skips |

On a fresh server the administrator still has its seeded password and is flagged `mustChangePassword`; the flag is advisory, so the harness signs in and keeps going. The harness runs with the server's default sign-in rate limit: its clients set `RateLimitRetries`, so a 429 waits out `Retry-After` (about a minute) instead of failing, and a full run takes a few minutes.

## Build

```bash
cd sdk/csharp/src
dotnet build Hugo.Sdk.sln     # zero warnings; warnings are errors
```
