namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Kubernetes.Tools;
    using k8s.Models;

    /// <summary>
    /// The <c>kubernetes</c> connector (tool family <c>k8s</c>): read, investigate, and change objects of any kind,
    /// including custom resources, within a namespace allow list. Authenticates with an inline kubeconfig or a service
    /// account token. Thread-safe; one instance serves every resource.
    /// </summary>
    public class KubernetesConnector : IConnector
    {
        #region Public-Members

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        #endregion

        #region Private-Members

        private static readonly Regex _NamespacePattern = new Regex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new KubernetesListTool(),
                new KubernetesGetTool(),
                new KubernetesDescribeTool(),
                new KubernetesEventsTool(),
                new KubernetesLogsTool(),
                new KubernetesTopTool(),
                new KubernetesRolloutStatusTool(),
                new KubernetesRolloutHistoryTool(),
                new KubernetesRolloutRestartTool(),
                new KubernetesScaleTool(),
                new KubernetesApplyTool(),
                new KubernetesDeleteTool(),
                new KubernetesExecTool(),
                new KubernetesCordonTool()
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
            KubernetesOptions options = KubernetesOptions.FromSettings(s);

            string? kubeconfig = resource.Secrets.Get(KubernetesSettingKeys.KubeconfigSlot, KubernetesSettingKeys.KubeconfigField);
            string? serviceToken = resource.Secrets.Get(KubernetesSettingKeys.ServiceAccountTokenSlot, KubernetesSettingKeys.TokenField);
            if (!String.IsNullOrWhiteSpace(kubeconfig))
            {
                KubeconfigInspection inspection = KubeconfigInspector.Inspect(kubeconfig, options.Context);
                if (!inspection.IsValid) result.Add(KubernetesSettingKeys.KubeconfigSlot, "invalid_format", inspection.ErrorText());
            }
            else if (!String.IsNullOrWhiteSpace(serviceToken))
            {
                if (String.IsNullOrWhiteSpace(options.ServerUrl)) result.Add(KubernetesSettingKeys.ServerUrl, "required", "Token authentication needs the API server URL, for example https://10.0.0.1:6443.");
            }
            else
            {
                result.Add(KubernetesSettingKeys.KubeconfigSlot, "required", "Bind a Kubeconfig secret to the 'kubeconfig' slot, or an ApiToken secret to the 'serviceAccountToken' slot with the serverUrl setting.");
            }

            if (!String.IsNullOrWhiteSpace(options.ServerUrl))
            {
                if (!Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out Uri? server) || (server.Scheme != Uri.UriSchemeHttps && server.Scheme != Uri.UriSchemeHttp))
                    result.Add(KubernetesSettingKeys.ServerUrl, "invalid_format", "serverUrl must be an absolute https:// URL, for example https://10.0.0.1:6443.");
            }

            try
            {
                KubernetesSessionFactory.LoadCertificateAuthority(resource);
            }
            catch (ConnectorException e)
            {
                result.Add(KubernetesSettingKeys.CertificateAuthoritySlot, "invalid_format", e.Message);
            }

            foreach (string ns in options.AllowedNamespaces)
            {
                if (!IsValidNamespace(ns))
                {
                    result.Add(KubernetesSettingKeys.AllowedNamespaces, "invalid_format", "'" + ns + "' is not a valid namespace name (lowercase letters, digits, and dashes, at most 63 characters).");
                    break;
                }
            }
            if (options.DefaultNamespace != null)
            {
                if (!IsValidNamespace(options.DefaultNamespace)) result.Add(KubernetesSettingKeys.DefaultNamespace, "invalid_format", "defaultNamespace is not a valid namespace name.");
                else if (!KubernetesNamespacePolicy.IsAllowed(options, options.DefaultNamespace)) result.Add(KubernetesSettingKeys.DefaultNamespace, "invalid_value", "defaultNamespace must be one of allowedNamespaces.");
            }

            CheckRange(result, s, KubernetesSettingKeys.MaxLogLines, 1, 100000);
            CheckRange(result, s, KubernetesSettingKeys.RequestTimeoutSeconds, 1, 600);
            CheckRange(result, s, KubernetesSettingKeys.MaxObjects, 1, 5000);
            CheckRange(result, s, KubernetesSettingKeys.ExecTimeoutSeconds, 1, 900);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            KubernetesOptions options = KubernetesOptions.FromSettings(resource.Settings);
            try
            {
                using (KubernetesSession session = await KubernetesSessionFactory.ConnectAsync(resource, options, token).ConfigureAwait(false))
                {
                    VersionInfo version = KubernetesFormat.Typed<VersionInfo>(await session.GetAsync("/version", "the server version", token).ConfigureAwait(false), "version");
                    string probeNamespace = KubernetesNamespacePolicy.Effective(options, null);
                    string probe = "/api/v1/namespaces/" + Uri.EscapeDataString(probeNamespace) + "/pods" + KubernetesSession.Query("limit", "1");
                    KubernetesApiResponse access = await session.SendAsync(System.Net.Http.HttpMethod.Get, probe, null, null, null, token).ConfigureAwait(false);
                    sw.Stop();
                    if ((int)access.StatusCode == 401)
                        return ConnectorHealthResult.Fail(ConnectorErrorCodes.AuthFailed, KubernetesErrorMapper.FromResponse(access, "pods").Message, sw.Elapsed.TotalMilliseconds);
                    ConnectorHealthResult health = ConnectorHealthResult.Ok("Connected to " + session.Server + " (Kubernetes " + version.GitVersion + ")", sw.Elapsed.TotalMilliseconds);
                    health.Details["server"] = session.Server;
                    health.Details["serverVersion"] = version.GitVersion ?? String.Empty;
                    health.Details["platform"] = version.Platform ?? String.Empty;
                    health.Details["probeNamespace"] = probeNamespace;
                    health.Details["canListPods"] = access.IsSuccess ? "true" : "false (HTTP " + (int)access.StatusCode + ")";
                    health.Details["allowedNamespaces"] = options.AllowedNamespaces.Count == 0 ? "all" : String.Join(",", options.AllowedNamespaces);
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
            catch (Exception e)
            {
                sw.Stop();
                ConnectorException mapped = KubernetesErrorMapper.FromException(e, "the connection test", options.ServerUrl);
                return ConnectorHealthResult.Fail(mapped.Code, mapped.Message, sw.Elapsed.TotalMilliseconds);
            }
        }

        #endregion

        #region Private-Methods

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.Context, SettingValueKindEnum.String, "Context") { Group = "Connection", ShowInSummary = true, Help = "Kubeconfig context to use. Empty uses the kubeconfig's current-context." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.ServerUrl, SettingValueKindEnum.String, "API server URL") { Group = "Connection", Pattern = "^https?://", Help = "Required with token authentication (for example https://10.0.0.1:6443). With a kubeconfig, overrides the context's server when set." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.InsecureSkipTlsVerify, SettingValueKindEnum.Boolean, "Skip TLS verification") { DefaultValue = "false", Group = "Connection", Help = "Accept any API server certificate. Only for disposable test clusters. Default false." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.RequestTimeoutSeconds, SettingValueKindEnum.Integer, "Request timeout (seconds)") { DefaultValue = "30", Minimum = 1, Maximum = 600, Group = "Connection", Help = "Timeout of each API request. Default 30, minimum 1, maximum 600." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.AllowedNamespaces, SettingValueKindEnum.StringList, "Allowed namespaces") { Group = "Scope", ShowInSummary = true, Help = "Namespaces this resource may read and change, one per line. Empty allows every namespace. Enforced on every call." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.DefaultNamespace, SettingValueKindEnum.String, "Default namespace") { Group = "Scope", Pattern = "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", Help = "Namespace used when a call names none. Empty uses the first allowed namespace, else default. Must be in allowedNamespaces when that is set." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.ClusterScopedReads, SettingValueKindEnum.Boolean, "Read cluster-scoped kinds") { DefaultValue = "true", Group = "Scope", Help = "Allow reading cluster-scoped kinds such as nodes, namespaces, and CRDs. Default true. Namespace objects outside allowedNamespaces stay hidden." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.AllowClusterScoped, SettingValueKindEnum.Boolean, "Change cluster-scoped kinds") { DefaultValue = "false", Group = "Scope", Help = "Allow changes to cluster-scoped kinds: cordoning nodes, and applying or deleting namespaces, cluster roles, CRDs, and storage classes. Default false." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.DestructiveKinds, SettingValueKindEnum.StringList, "Destructive kinds") { Group = "Safety", Help = "Kinds whose apply is always Destructive, one per line. Empty uses the defaults: " + String.Join(", ", KubernetesOptions.DefaultDestructiveKinds) + "." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.FieldManager, SettingValueKindEnum.String, "Field manager") { DefaultValue = "hugo", Group = "Safety", Help = "Field manager name for server-side apply and patches. Default hugo." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.MaxLogLines, SettingValueKindEnum.Integer, "Max log lines") { DefaultValue = "500", Minimum = 1, Maximum = 100000, Group = "Limits", Help = "Most lines k8s_logs returns. Default 500." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.MaxObjects, SettingValueKindEnum.Integer, "Max objects per list") { DefaultValue = "200", Minimum = 1, Maximum = 5000, Group = "Limits", Help = "Most objects k8s_list returns in one call (page with continue). Default 200." });
            schema.Add(new SettingDescriptor(KubernetesSettingKeys.ExecTimeoutSeconds, SettingValueKindEnum.Integer, "Exec timeout (seconds)") { DefaultValue = "60", Minimum = 1, Maximum = 900, Group = "Limits", Help = "Default and maximum run time of k8s_exec. Default 60." });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = "kubernetes";
            d.Family = "k8s";
            d.DisplayName = "Kubernetes";
            d.Description = "Kubernetes clusters: list, get, and describe any kind including custom resources, read events, logs, and metrics, check and restart rollouts, scale, apply manifests with server-side apply, delete, exec, and cordon nodes, all inside a namespace allow list. Secret values are always redacted.";
            d.Category = ConnectorCategoryEnum.Orchestration;
            d.IconKey = "cluster";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(KubernetesSettingKeys.KubeconfigSlot, "Kubeconfig", SecretKindEnum.Kubeconfig) { Help = "A kubeconfig with inline credentials (token or client-certificate-data). Exec and auth-provider plugins and file references are refused. Takes precedence over serviceAccountToken." },
                new SecretSlotDescriptor(KubernetesSettingKeys.ServiceAccountTokenSlot, "Service account token", SecretKindEnum.ApiToken) { Help = "A bearer token (field 'token'), usually a service account token. Needs the serverUrl setting." },
                new SecretSlotDescriptor(KubernetesSettingKeys.CertificateAuthoritySlot, "Cluster CA certificate", SecretKindEnum.Certificate) { Help = "PEM CA certificate of the API server (field 'certificate'), used with token authentication." }
            };
            d.DefaultInstructionsKey = "connector.kubernetes.instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(KubernetesConnector).Assembly, "connector.kubernetes.instructions.md");
            d.DocumentationUrl = "https://kubernetes.io/docs/reference/kubernetes-api/";
            return d;
        }

        private static bool IsValidNamespace(string ns)
        {
            return ns.Length <= 63 && _NamespacePattern.IsMatch(ns);
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
