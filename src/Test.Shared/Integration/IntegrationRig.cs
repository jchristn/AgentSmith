namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database;
    using Hugo.Core.Database.Ado;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Serialization;
    using Hugo.Server;
    using Hugo.Server.Settings;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// A running server with the echo connector and an activated anderson@hugo.local client, plus helpers for the
    /// integration suites (resources, bindings, invocations, approvals) over the flat REST routes.
    /// </summary>
    public sealed class IntegrationRig : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Server host.
        /// </summary>
        public TestServerHost Host { get; }

        /// <summary>
        /// Client signed in as anderson@hugo.local (tenant administrator).
        /// </summary>
        public HttpClient Anderson { get; }

        /// <summary>
        /// The default profile (Hugo).
        /// </summary>
        public AgentProfile Hugo { get; }

        /// <summary>
        /// Server context.
        /// </summary>
        public ServerContext Context
        {
            get
            {
                return Host.Server.Context;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return Host.TenantId;
            }
        }

        #endregion

        #region Constructors-and-Factories

        private IntegrationRig(TestServerHost host, HttpClient anderson, AgentProfile hugo)
        {
            Host = host;
            Anderson = anderson;
            Hugo = hugo;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a server and sign in Anderson.
        /// </summary>
        /// <param name="provider">Database provider.</param>
        /// <param name="configure">Settings changes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rig.</returns>
        public static async Task<IntegrationRig> StartAsync(DatabaseTypeEnum provider, Action<HugoSettings>? configure, CancellationToken token)
        {
            TestServerHost host = await TestServerHost.StartAsync(provider, configure, token).ConfigureAwait(false);
            HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false);
            EnumerationResult<AgentProfile> profiles = await HttpTestHelpers.SendAsync<EnumerationResult<AgentProfile>>(anderson, HttpMethod.Get, "/v1.0/api/agent-profiles", null, 200, token).ConfigureAwait(false);
            AgentProfile hugo = profiles.Objects.FirstOrDefault(p => p.Name == "Hugo") ?? throw new TestAssertionException("The default profile Hugo was not provisioned.");
            return new IntegrationRig(host, anderson, hugo);
        }

        /// <summary>
        /// Create a secret.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="kind">Kind.</param>
        /// <param name="values">Field values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Secret.</returns>
        public async Task<Secret> SecretAsync(string name, SecretKindEnum kind, Dictionary<string, string> values, CancellationToken token)
        {
            SecretRequest request = new SecretRequest();
            request.Name = name;
            request.Kind = kind;
            request.Values = values;
            return await HttpTestHelpers.SendAsync<Secret>(Anderson, HttpMethod.Post, "/v1.0/api/secrets", request, 201, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create an echo (or other) resource.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="connectorType">Connector type. Default echo.</param>
        /// <param name="settings">Settings, or null.</param>
        /// <param name="secretId">Secret bound to the token slot, or null.</param>
        /// <param name="ceiling">Access mode ceiling, or null.</param>
        /// <returns>Resource.</returns>
        public async Task<Resource> ResourceAsync(string name, CancellationToken token, string connectorType = "echo", Dictionary<string, object>? settings = null, string? secretId = null, AccessModeEnum? ceiling = null)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["name"] = name;
            body["connectorType"] = connectorType;
            body["settings"] = settings ?? new Dictionary<string, object>();
            if (secretId != null) body["secretBindings"] = new List<object> { new Dictionary<string, string> { { "slotName", "token" }, { "secretId", secretId } } };
            if (ceiling.HasValue) body["defaultAccessModeCeiling"] = ceiling.Value.ToString();
            return await HttpTestHelpers.SendAsync<Resource>(Anderson, HttpMethod.Post, "/v1.0/api/resources", body, 201, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Bind a resource to a profile.
        /// </summary>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="mode">Access mode.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="configure">Further request changes.</param>
        /// <returns>Binding.</returns>
        public async Task<Binding> BindAsync(string profileId, string resourceId, AccessModeEnum mode, CancellationToken token, Action<BindingRequest>? configure = null)
        {
            BindingRequest request = new BindingRequest();
            request.ResourceId = resourceId;
            request.AccessMode = mode;
            configure?.Invoke(request);
            return await HttpTestHelpers.SendAsync<Binding>(Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + profileId + "/bindings", request, 201, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a binding.
        /// </summary>
        /// <param name="binding">Binding.</param>
        /// <param name="configure">Request changes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding.</returns>
        public async Task<Binding> UpdateBindingAsync(Binding binding, Action<BindingRequest> configure, CancellationToken token)
        {
            BindingRequest request = new BindingRequest();
            configure(request);
            return await HttpTestHelpers.SendAsync<Binding>(Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + binding.AgentProfileId + "/bindings/" + binding.Id, request, 200, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Invoke a tool as a profile over REST.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <param name="alias">Resource alias.</param>
        /// <param name="arguments">Arguments object, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="expectedStatus">Expected HTTP status, or null for 200 or 202.</param>
        /// <param name="client">Client; default Anderson.</param>
        /// <param name="profile">Profile identifier or name; default the resolved profile.</param>
        /// <param name="dryRun">Dry run.</param>
        /// <returns>Outcome.</returns>
        public async Task<ToolInvocationOutcome> InvokeAsync(string tool, string alias, object? arguments, CancellationToken token, int? expectedStatus = null, HttpClient? client = null, string? profile = null, bool dryRun = false)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["toolName"] = tool;
            body["resource"] = alias;
            body["arguments"] = arguments ?? new Dictionary<string, object>();
            if (profile != null) body["profile"] = profile;
            TestResponse response = await HttpTestHelpers.SendAsync(client ?? Anderson, HttpMethod.Post, dryRun ? "/v1.0/api/tool-invocations/dry-run" : "/v1.0/api/tool-invocations", body, token).ConfigureAwait(false);
            if (expectedStatus.HasValue && response.StatusCode != expectedStatus.Value) throw new TestAssertionException("Invoke " + tool + " returned " + response.StatusCode + " (expected " + expectedStatus.Value + "): " + response.Body);
            if (!expectedStatus.HasValue && response.StatusCode != 200 && response.StatusCode != 202) throw new TestAssertionException("Invoke " + tool + " returned " + response.StatusCode + ": " + response.Body);
            return Serializer.Deserialize<ToolInvocationOutcome>(response.Body) ?? throw new TestAssertionException("Empty invoke response.");
        }

        /// <summary>
        /// Decide an approval over REST.
        /// </summary>
        /// <param name="approvalId">Approval identifier.</param>
        /// <param name="action">approve, reject, or cancel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="expectedStatus">Expected status. Default 200.</param>
        /// <param name="client">Client; default Anderson.</param>
        /// <returns>Response.</returns>
        public async Task<TestResponse> DecideAsync(string approvalId, string action, CancellationToken token, int expectedStatus = 200, HttpClient? client = null)
        {
            TestResponse response = await HttpTestHelpers.SendAsync(client ?? Anderson, HttpMethod.Post, "/v1.0/api/approvals/" + approvalId + "/" + action, new { note = "test " + action }, token).ConfigureAwait(false);
            if (response.StatusCode != expectedStatus) throw new TestAssertionException(action + " " + approvalId + " returned " + response.StatusCode + " (expected " + expectedStatus + "): " + response.Body);
            return response;
        }

        /// <summary>
        /// Read a tool invocation record from the database (waits for the background recorder).
        /// </summary>
        /// <param name="id">Invocation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record.</returns>
        public async Task<ToolInvocation> RecordAsync(string id, CancellationToken token)
        {
            await Context.Get<Hugo.Server.Services.ToolInvocationRecorder>().FlushAsync(token).ConfigureAwait(false);
            return await Context.Database.ToolInvocations.ReadAsync(TenantId, id, token).ConfigureAwait(false) ?? throw new TestAssertionException("Tool invocation " + id + " was not recorded.");
        }

        /// <summary>
        /// Run raw SQL against the server database (test setup only).
        /// </summary>
        /// <param name="sql">SQL with @p0 style parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="values">Parameter values (@p0, @p1, ...).</param>
        /// <returns>Rows affected.</returns>
        public async Task<int> SqlAsync(string sql, CancellationToken token, params object?[] values)
        {
            AdoDatabaseDriver driver = (AdoDatabaseDriver)Context.Database;
            return await driver.NonQueryAsync(sql, command =>
            {
                for (int i = 0; i < values.Length; i++) AdoDatabaseDriver.Add(command, "p" + i, values[i]);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Audit records of one event type in the rig's tenant.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records, newest first.</returns>
        public async Task<List<AuditRecord>> AuditAsync(AuditEventTypeEnum eventType, CancellationToken token)
        {
            AuditFilter filter = new AuditFilter();
            filter.TenantId = TenantId;
            filter.EventType = eventType;
            filter.PageSize = 1000;
            AuditPage page = await Context.Database.Audit.EnumerateAsync(filter, token).ConfigureAwait(false);
            return page.Objects;
        }

        /// <summary>
        /// Qualified table reference.
        /// </summary>
        /// <param name="rawName">Table name.</param>
        /// <returns>Reference.</returns>
        public string Table(string rawName)
        {
            return ((AdoDatabaseDriver)Context.Database).Table(rawName);
        }

        /// <summary>
        /// Stop the server.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            Anderson.Dispose();
            await Host.DisposeAsync().ConfigureAwait(false);
        }

        #endregion
    }
}
