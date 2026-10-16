namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Resource health sweep eligibility, isolation, cancellation, observations, and safe scheduled probes.
    /// </summary>
    public static class ResourceHealthSuite
    {
        /// <summary>Build tests for each configured provider.</summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "resource-health-" + provider.ToString().ToLowerInvariant();
                suites.Add(new TestSuiteDescriptor(id, "Resource health (" + provider + ")", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "sweep", "Health sweep scopes active tenants/resources, isolates failures, rejects learning, and emits aggregate gauges", ct => SweepAsync(provider, ct)),
                    new TestCaseDescriptor(id, "periodic", "Background health loop periodically probes resources and shuts down cleanly", ct => PeriodicAsync(provider, ct)),
                    new TestCaseDescriptor(id, "timeout", "Scheduled health bounds noncooperative probes, prevents overlap, and retains leases until completion", ct => TimeoutAsync(provider, ct))
                }));
            }
            return suites;
        }

        private static async Task SweepAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            HealthProbeConnector connector = new HealthProbeConnector();
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => { s.Connectors.HealthIntervalSeconds = 86400; s.Connectors.TestTimeoutSeconds = 1; }, token, connectors: new[] { connector }).ConfigureAwait(false))
            {
                ResourceHealthService health = host.Server.Context.Get<ResourceHealthService>();
                ResourceService resources = host.Server.Context.Get<ResourceService>();
                Resource good = await AddAsync(host, host.TenantId, "healthy", "healthprobe", true, token).ConfigureAwait(false);
                await AddAsync(host, host.TenantId, "fails", "healthprobe", true, token).ConfigureAwait(false);
                await AddAsync(host, host.TenantId, "throws", "healthprobe", true, token).ConfigureAwait(false);
                Resource learning = await AddAsync(host, host.TenantId, "learns", "healthprobe", true, token).ConfigureAwait(false);
                await AddAsync(host, host.TenantId, "disabled", "healthprobe", false, token).ConfigureAwait(false);
                await AddAsync(host, host.TenantId, "unregistered", "not-allowed", true, token).ConfigureAwait(false);
                Tenant other = await host.Server.Context.Database.Tenants.CreateAsync(new Tenant { Name = "other" }, token).ConfigureAwait(false);
                Resource foreign = await AddAsync(host, other.Id, "healthy", "healthprobe", true, token).ConfigureAwait(false);
                Tenant inactive = await host.Server.Context.Database.Tenants.CreateAsync(new Tenant { Name = "inactive", Active = false }, token).ConfigureAwait(false);
                await AddAsync(host, inactive.Id, "healthy", "healthprobe", true, token).ConfigureAwait(false);

                Assert.Equal(0, await health.SweepAsync(token).ConfigureAwait(false), "Expensive/custom connectors disabled by default");
                Assert.Equal(0, connector.Calls.Count, "Excluded connector must not run");
                host.Settings.Connectors.HealthConnectorTypes = " healthprobe ";
                Assert.Equal(5, await health.SweepAsync(token).ConfigureAwait(false), "Eligible active resources");
                Assert.Equal(5, connector.Calls.Count, "Inactive tenants/resources and disallowed types excluded");
                Assert.True(connector.Calls.ContainsKey(other.Id + ":" + foreign.Id), "Other tenant resource uses its own tenant context.");
                Assert.Equal(2.0, Count(health, "healthy"), "Healthy gauge count across tenants");
                Assert.Equal(3.0, Count(health, "unhealthy"), "Failed, thrown, and learning probe gauge count");
                using (MeterListener listener = new MeterListener())
                {
                    List<double> observed = new List<double>();
                    listener.InstrumentPublished = (instrument, source) =>
                    {
                        if (instrument.Meter.Name == "Hugo" && instrument.Name == HugoMetricCatalog.ResourceHealth.Name) source.EnableMeasurementEvents(instrument);
                    };
                    listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
                    {
                        Assert.Equal(2, tags.Length, "Resource gauge label cardinality");
                        Assert.True(tags.ToArray().All(t => t.Key == "connector" || t.Key == "status"), "Resource identifiers leaked into metric labels.");
                        observed.Add(value);
                    });
                    listener.Start();
                    listener.RecordObservableInstruments();
                    Assert.Equal(2, observed.Count, "Registered observable gauge emits both statuses");
                    Assert.Equal(5.0, observed.Sum(), "Registered gauge resource count");
                }
                Resource saved = (await host.Server.Context.Database.Resources.ReadAsync(host.TenantId, learning.Id, token).ConfigureAwait(false))!;
                Assert.True(!saved.Settings.Any(s => s.Key == "hostKeyFingerprint"), "Scheduled probe persisted learned trust.");
                Assert.True(saved.LastTestResult == false, "Learning probe must fail rather than appear healthy.");
                Assert.True((await host.Server.Context.Database.Resources.ReadAsync(host.TenantId, good.Id, token).ConfigureAwait(false))!.LastTestResult == true, "Probe result persisted.");
                Assert.True(await resources.TestScheduledAsync(other.Id, good.Id, token).ConfigureAwait(false) == null, "Foreign resource identifier accepted.");

                host.Settings.Connectors.HealthIntervalSeconds = 0;
                Assert.True(!health.Observe().Any(), "Disabling sweeps leaves stale metrics.");
                Assert.Equal(0, await health.SweepAsync(token).ConfigureAwait(false), "Disabled sweep ran probes");
                host.Settings.Connectors.HealthIntervalSeconds = 86400;
                await health.SweepAsync(token).ConfigureAwait(false);
                host.AdminClient.DefaultRequestHeaders.Add("x-tenant-guid", host.TenantId);
                Assert.Equal(204, (await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Delete, "/v1.0/api/resources/" + good.Id, null, token).ConfigureAwait(false)).StatusCode, "Resource deletion");
                Assert.True(!health.Observe().Any(), "Removed resource remains in gauge snapshot.");
                await health.SweepAsync(token).ConfigureAwait(false);
                Assert.Equal(1.0, Count(health, "healthy"), "Deleted resource excluded from rebuilt snapshot");
                host.AdminClient.DefaultRequestHeaders.Remove("x-tenant-guid");
                host.AdminClient.DefaultRequestHeaders.Add("x-tenant-guid", other.Id);
                Assert.Equal(200, (await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Put, "/v1.0/api/resources/" + foreign.Id, new { active = false }, token).ConfigureAwait(false)).StatusCode, "Disable resource");
                Assert.True(!health.Observe().Any(), "Disabled resource remains in gauge snapshot.");
                await health.SweepAsync(token).ConfigureAwait(false);
                Assert.Equal(0.0, Count(health, "healthy"), "Disabled resource excluded from rebuilt snapshot");
                Resource ssh = await AddAsync(host, host.TenantId, "unpinned-ssh", "ssh", true, token).ConfigureAwait(false);
                Hugo.Core.Responses.ResourceTestResponse? sshProbe = await resources.TestScheduledAsync(host.TenantId, ssh.Id, token).ConfigureAwait(false);
                Assert.Equal("scheduled_learning_disabled", sshProbe!.ErrorCode, "Scheduled SSH must reject TOFU before connecting");
                Resource mismatch = await AddAsync(host, host.TenantId, "host-mismatch", "healthprobe", true, token).ConfigureAwait(false);
                await resources.TestScheduledAsync(host.TenantId, mismatch.Id, token).ConfigureAwait(false);
                Hugo.Core.Responses.AuditPage audit = await host.Server.Context.Database.Audit.EnumerateAsync(new Hugo.Core.Requests.AuditFilter { TenantId = host.TenantId, EventType = AuditEventTypeEnum.HostKeyMismatch }, token).ConfigureAwait(false);
                Assert.True(audit.Objects.Any(r => r.ResourceId == mismatch.Id), "Scheduled host mismatch audit lost saved resource identity.");
                host.Settings.Connectors.HealthConnectorTypes = String.Empty;
                Assert.True(!health.Observe().Any(), "Removed connector remains in gauge snapshot.");
                Assert.Equal("hugo_resource_health", HugoMetricCatalog.ResourceHealth.PrometheusName, "Grafana series contract");
            }
        }

        private static async Task PeriodicAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            HealthProbeConnector connector = new HealthProbeConnector();
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => { s.Connectors.HealthIntervalSeconds = 1; s.Connectors.HealthConnectorTypes = "healthprobe"; }, token, connectors: new[] { connector }).ConfigureAwait(false))
            {
                Resource resource = await AddAsync(host, host.TenantId, "healthy", "healthprobe", true, token).ConfigureAwait(false);
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    while (true)
                    {
                        Resource? saved = await host.Server.Context.Database.Resources.ReadAsync(host.TenantId, resource.Id, timeout.Token).ConfigureAwait(false);
                        if (saved?.LastTestResult == true) break;
                        await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                    }
                }
                Assert.True(connector.Calls[host.TenantId + ":" + resource.Id] >= 1, "Periodic loop did not call connector.");
            }
        }

        private static async Task TimeoutAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            HealthProbeConnector connector = new HealthProbeConnector();
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => { s.Connectors.HealthIntervalSeconds = 86400; s.Connectors.HealthConnectorTypes = "healthprobe"; s.Connectors.TestTimeoutSeconds = 1; }, token, connectors: new[] { connector }).ConfigureAwait(false))
            {
                ResourceHealthService health = host.Server.Context.Get<ResourceHealthService>();
                ResourceService resources = host.Server.Context.Get<ResourceService>();
                Resource blocked = await AddAsync(host, host.TenantId, "ignores-cancellation", "healthprobe", true, token).ConfigureAwait(false);
                Resource slow = await AddAsync(host, host.TenantId, "timeout", "healthprobe", true, token).ConfigureAwait(false);
                try
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    Assert.Equal(2, await health.SweepAsync(token).ConfigureAwait(false), "Timed-out resources counted");
                    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Noncooperative connector blocked sweep.");
                    Assert.Equal(2.0, Count(health, "unhealthy"), "Timeout gauge");
                    await resources.TestScheduledAsync(host.TenantId, blocked.Id, token).ConfigureAwait(false);
                    Assert.Equal(1, connector.Calls[host.TenantId + ":" + blocked.Id], "Noncooperative probes overlap");
                    using (CancellationTokenSource cancelled = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        cancelled.Cancel();
                        bool threw = false;
                        try { await health.SweepAsync(cancelled.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { threw = true; }
                        Assert.True(threw, "Cancelled sweep ignored cancellation.");
                        Assert.Equal(2.0, Count(health, "unhealthy"), "Cancellation must retain last valid snapshot");
                    }
                }
                finally
                {
                    connector.Release.TrySetResult(true);
                    Assert.True(await connector.LeaseRead.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false), "Probe lease disposed before late completion.");
                }
                Assert.True((await host.Server.Context.Database.Resources.ReadAsync(host.TenantId, slow.Id, token).ConfigureAwait(false))!.LastTestResult == false, "Timeout result persisted.");
                using (CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cancel.CancelAfter(100);
                    bool threw = false;
                    try { await resources.TestScheduledAsync(host.TenantId, slow.Id, cancel.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { threw = true; }
                    Assert.True(threw, "In-progress probe ignored shutdown cancellation.");
                }
            }
        }

        private static double Count(ResourceHealthService health, string status)
        {
            return health.Observe().Where(m => m.Tags.ToArray().Any(t => t.Key == "status" && Equals(t.Value, status))).Sum(m => m.Value);
        }

        private static async Task<Resource> AddAsync(TestServerHost host, string tenantId, string name, string type, bool active, CancellationToken token)
        {
            return await host.Server.Context.Database.Resources.CreateAsync(new Resource { TenantId = tenantId, Name = name, ConnectorType = type, Active = active }, token).ConfigureAwait(false);
        }
    }
}
