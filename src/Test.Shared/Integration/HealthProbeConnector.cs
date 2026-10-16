namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Controlled health connector for sweep tests, including cancellation-cooperative and noncooperative probes.
    /// </summary>
    public class HealthProbeConnector : IConnector
    {
        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; } = new ConnectorDescriptor { Type = "healthprobe", Family = "healthprobe", DisplayName = "Health probe" };

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; } = Array.Empty<IConnectorTool>();

        /// <summary>Invocations keyed by tenant and resource identifiers.</summary>
        public ConcurrentDictionary<string, int> Calls { get; } = new ConcurrentDictionary<string, int>();

        /// <summary>Releases a deliberately noncooperative probe.</summary>
        public TaskCompletionSource<bool> Release { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Signals that the noncooperative probe has safely read its still-live lease after timeout.</summary>
        public TaskCompletionSource<bool> LeaseRead { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return Task.FromResult(new ConnectorValidationResult());
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            Calls.AddOrUpdate(resource.TenantId + ":" + resource.ResourceId, 1, (key, count) => count + 1);
            switch (resource.Name)
            {
                case "throws": throw new InvalidOperationException("Controlled probe failure.");
                case "host-mismatch": return ConnectorHealthResult.Fail(ConnectorErrorCodes.HostKeyMismatch, "Controlled host key mismatch.", 1);
                case "fails": return ConnectorHealthResult.Fail("offline", "Controlled offline resource.", 1);
                case "learns": resource.LearnSetting?.Invoke("hostKeyFingerprint", "unapproved"); break;
                case "timeout": await Task.Delay(TimeSpan.FromMinutes(5), token).ConfigureAwait(false); break;
                case "ignores-cancellation":
                    await Release.Task.ConfigureAwait(false);
                    try
                    {
                        resource.Secrets.Get("unused", "unused");
                        LeaseRead.TrySetResult(true);
                    }
                    catch (Exception e)
                    {
                        LeaseRead.TrySetException(e);
                    }
                    break;
            }
            return ConnectorHealthResult.Ok("Connected.", 1);
        }
    }
}
