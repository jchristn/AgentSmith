namespace Hugo.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Docker.Tools;

    /// <summary>
    /// The <c>docker</c> connector (tool family <c>docker</c>): containers, images, logs, stats, and events on one Docker
    /// daemon over a Unix socket, a named pipe, or TCP (optionally with TLS client authentication). Thread-safe; one
    /// instance serves every resource.
    /// </summary>
    public class DockerConnector : IConnector
    {
        #region Public-Members

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new DockerPsTool(),
                new DockerInspectTool(),
                new DockerLogsTool(),
                new DockerStatsTool(),
                new DockerImagesTool(),
                new DockerEventsTool(),
                new DockerContainerActionTool(),
                new DockerExecTool()
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            ConnectorValidationResult result = new ConnectorValidationResult();
            ResourceSettings s = resource.Settings;
            DockerOptions options = DockerOptions.FromSettings(s);

            Uri? endpoint = null;
            try
            {
                endpoint = DockerSessionFactory.ParseEndpoint(options.Endpoint);
            }
            catch (ConnectorException e)
            {
                result.Add(DockerSettingKeys.Endpoint, e.Code == ConnectorErrorCodes.NotSupported ? "not_supported" : "invalid_format", e.Message);
            }

            bool hasCertificate = !String.IsNullOrWhiteSpace(resource.Secrets.Get(DockerSettingKeys.TlsClientCertificateSlot, DockerSettingKeys.CertificateField));
            if (options.TlsVerify && !hasCertificate)
                result.Add(DockerSettingKeys.TlsClientCertificateSlot, "required", "tlsVerify is on, so bind a Certificate secret (certificate, privateKey, caCertificate) to the 'tlsClientCertificate' slot.");
            if (hasCertificate)
            {
                if (endpoint != null && endpoint.Scheme != "tcp" && endpoint.Scheme != "https")
                    result.Add(DockerSettingKeys.Endpoint, "invalid_value", "A TLS client certificate is only used with a tcp:// endpoint.");
                try
                {
                    DockerSessionFactory.LoadCredentials(resource, options.TlsVerify);
                }
                catch (ConnectorException e)
                {
                    result.Add(DockerSettingKeys.TlsClientCertificateSlot, "invalid_format", e.Message);
                }
            }

            CheckRange(result, s, DockerSettingKeys.RequestTimeoutSeconds, 1, 600);
            CheckRange(result, s, DockerSettingKeys.MaxLogLines, 1, 100000);
            CheckRange(result, s, DockerSettingKeys.MaxEventsWindowSeconds, 60, 604800);
            CheckRange(result, s, DockerSettingKeys.ExecTimeoutSeconds, 1, 900);
            CheckRange(result, s, DockerSettingKeys.MaxStatsContainers, 1, 100);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            DockerOptions options = DockerOptions.FromSettings(resource.Settings);
            try
            {
                using (DockerSession session = await DockerSessionFactory.ConnectAsync(resource, options, token).ConfigureAwait(false))
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    limit.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
                    VersionResponse version = await session.Client.System.GetVersionAsync(limit.Token).ConfigureAwait(false);
                    sw.Stop();
                    ConnectorHealthResult health = ConnectorHealthResult.Ok("Connected to Docker " + version.Version + " at " + options.Endpoint, sw.Elapsed.TotalMilliseconds);
                    health.Details["endpoint"] = options.Endpoint;
                    health.Details["serverVersion"] = version.Version ?? String.Empty;
                    health.Details["apiVersion"] = version.APIVersion ?? String.Empty;
                    health.Details["os"] = (version.Os ?? String.Empty) + "/" + (version.Arch ?? String.Empty);
                    health.Details["tls"] = DockerSessionFactory.LoadCredentials(resource, options.TlsVerify) == null ? "false" : (options.TlsVerify ? "verified" : "unverified");
                    return health;
                }
            }
            catch (ConnectorException e)
            {
                sw.Stop();
                return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "The Docker daemon at " + options.Endpoint + " did not answer within " + options.RequestTimeoutSeconds + " seconds.", sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception e)
            {
                sw.Stop();
                ConnectorException mapped = DockerErrorMapper.Map(e, "the connection test", options.Endpoint);
                return ConnectorHealthResult.Fail(mapped.Code, mapped.Message, sw.Elapsed.TotalMilliseconds);
            }
        }

        #endregion

        #region Private-Methods

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor(DockerSettingKeys.Endpoint, SettingValueKindEnum.String, "Endpoint") { Required = true, DefaultValue = "unix:///var/run/docker.sock", Group = "Connection", ShowInSummary = true, Pattern = "^(unix|tcp|npipe|http|https)://", Help = "Daemon address: unix:///var/run/docker.sock (the Hugo host's socket), tcp://host:2376 (TLS) or tcp://host:2375, http://host:2375 (plain HTTP, the same as tcp:// without TLS), https://host:2376 (TLS), or npipe://./pipe/docker_engine. Reaching a daemon through an SSH resource is not supported yet." });
            schema.Add(new SettingDescriptor(DockerSettingKeys.TlsVerify, SettingValueKindEnum.Boolean, "Verify TLS") { DefaultValue = "false", Group = "Connection", Help = "Verify the daemon's certificate against the caCertificate in the tlsClientCertificate secret (or the system trust store). Requires the tlsClientCertificate slot. Default false." });
            schema.Add(new SettingDescriptor(DockerSettingKeys.RequestTimeoutSeconds, SettingValueKindEnum.Integer, "Request timeout (seconds)") { DefaultValue = "30", Minimum = 1, Maximum = 600, Group = "Connection", Help = "Timeout of each daemon request. Default 30, minimum 1, maximum 600." });
            schema.Add(new SettingDescriptor(DockerSettingKeys.ShowEnvironmentValues, SettingValueKindEnum.Boolean, "Show environment values") { DefaultValue = "false", Group = "Safety", Help = "When false, docker_inspect replaces environment variable values with <redacted> (names stay visible). Turn on only when containers carry no secrets in their environment. Default false." });
            schema.Add(new SettingDescriptor(DockerSettingKeys.MaxLogLines, SettingValueKindEnum.Integer, "Max log lines") { DefaultValue = "500", Minimum = 1, Maximum = 100000, Group = "Limits", Help = "Most lines docker_logs returns. Default 500." });
            schema.Add(new SettingDescriptor(DockerSettingKeys.MaxEventsWindowSeconds, SettingValueKindEnum.Integer, "Max events window (seconds)") { DefaultValue = "3600", Minimum = 60, Maximum = 604800, Group = "Limits", Help = "Largest time window docker_events reads; a longer window is refused with invalid_argument. Default 3600 (one hour)." });
            schema.Add(new SettingDescriptor(DockerSettingKeys.ExecTimeoutSeconds, SettingValueKindEnum.Integer, "Exec timeout (seconds)") { DefaultValue = "60", Minimum = 1, Maximum = 900, Group = "Limits", Help = "Default and maximum run time of docker_exec. Default 60." });
            schema.Add(new SettingDescriptor(DockerSettingKeys.MaxStatsContainers, SettingValueKindEnum.Integer, "Max containers per stats call") { DefaultValue = "20", Minimum = 1, Maximum = 100, Group = "Limits", Help = "Most containers docker_stats samples when no container is named. Default 20." });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = "docker";
            d.Family = "docker";
            d.DisplayName = "Docker host";
            d.Description = "A Docker daemon: list and inspect containers and images, read logs, one-shot stats, and bounded event windows, start, restart, stop, kill, or remove containers, and exec commands. Environment values are redacted by default.";
            d.Category = ConnectorCategoryEnum.Orchestration;
            d.IconKey = "container";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(DockerSettingKeys.TlsClientCertificateSlot, "TLS client certificate", SecretKindEnum.Certificate) { Help = "For tcp:// endpoints with TLS: the client certificate (field 'certificate'), its 'privateKey', and the 'caCertificate' that signed the daemon's certificate, all PEM." }
            };
            d.DefaultInstructionsKey = "connector.docker.instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(DockerConnector).Assembly, "connector.docker.instructions.md");
            d.DocumentationUrl = "https://docs.docker.com/reference/api/engine/";
            return d;
        }

        private static void CheckRange(ConnectorValidationResult result, ResourceSettings s, string key, long min, long max)
        {
            if (!s.Has(key)) return;
            long value = s.GetLong(key, Int64.MinValue);
            if (value == Int64.MinValue || value < min || value > max) result.Add(key, "out_of_range", key + " must be between " + min + " and " + max + ".");
        }

        #endregion
    }
}
