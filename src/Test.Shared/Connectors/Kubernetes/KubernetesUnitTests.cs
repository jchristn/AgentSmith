namespace Test.Shared.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Authentication;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Kubernetes;
    using k8s.Models;
    using Test.Shared.Connectors.Ssh;
    using Touchstone.Core;

    /// <summary>
    /// Kubernetes connector unit cases (no cluster): descriptor, schemas, settings and validation, kubeconfig safety,
    /// namespace policy, per-tool classification, Secret redaction, manifest parsing, YAML output, field-removal
    /// detection, rollout rules, and error mapping.
    /// </summary>
    public static class KubernetesUnitTests
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "connectors.kubernetes";

        #endregion

        #region Private-Members

        private static readonly KubernetesConnector _Connector = new KubernetesConnector();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the unit cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "descriptor", "Descriptor has type, family, category, icon, slots, and instructions", Descriptor);
            Add(cases, "settings-schema", "Settings schema lists every documented key with defaults and ranges", SettingsSchema);
            Add(cases, "tools", "Tool set, names, base effects, and per-call flags", ToolsShape);
            Add(cases, "tool-schemas-parse", "Every tool input schema parses and every property is described", ToolSchemas);
            Add(cases, "options-defaults", "Options apply defaults, parse lists, and clamp ranges", OptionsDefaults);
            Add(cases, "validate-credentials", "Validation requires a kubeconfig, or a token with serverUrl", ValidateCredentials);
            Add(cases, "validate-settings", "Validation checks namespaces, defaultNamespace, serverUrl, CA, and ranges", ValidateSettings);
            Add(cases, "kubeconfig-safety", "Kubeconfigs with exec, auth-provider, or file references are refused", KubeconfigSafety);
            Add(cases, "kubeconfig-contexts", "Kubeconfig context selection and errors", KubeconfigContexts);
            Add(cases, "session-configuration", "Client configuration from kubeconfig and from token plus CA", SessionConfiguration);
            Add(cases, "namespace-policy", "Namespace allow list, defaults, and cluster-scoped policy", NamespacePolicy);
            Add(cases, "known-kinds", "Well-known kinds resolve by name, short name, and apiVersion", KnownKinds);
            Add(cases, "resource-paths", "REST paths for core, grouped, namespaced, cluster-scoped, and subresources", ResourcePaths);
            Add(cases, "classify-read-tools", "Read tools classify as Read and reject namespaces outside the allow list", ClassifyReadTools);
            Add(cases, "classify-write-tools", "Write and Destructive tools classify per call with summaries", ClassifyWriteTools);
            Add(cases, "classify-cluster-scoped", "Cluster-scoped changes need allowClusterScoped; reads need clusterScopedReads", ClassifyClusterScoped);
            Add(cases, "secret-redaction", "Secret data, stringData, and last-applied annotations are always redacted", SecretRedaction);
            Add(cases, "secret-redaction-lists", "Secrets inside lists and nested documents are redacted", SecretRedactionLists);
            Add(cases, "describe-secret-offline", "k8s_describe output for a Secret shows sizes, never values", DescribeSecretOffline);
            Add(cases, "manifest-parser", "Manifests parse YAML documents, JSON, lists, and typed scalars", ManifestParser);
            Add(cases, "manifest-parser-errors", "Manifest parser rejects missing fields, too many objects, and merge keys", ManifestParserErrors);
            Add(cases, "yaml-writer", "YAML output round-trips through the parser, including tricky strings", YamlWriter);
            Add(cases, "field-diff", "Removed fields are detected; status and server metadata are ignored", FieldDiff);
            Add(cases, "rollout-rules", "Rollout status follows kubectl's rules", RolloutRules);
            Add(cases, "error-mapping-status", "HTTP statuses map to standard error codes", ErrorMappingStatus);
            Add(cases, "error-mapping-exceptions", "Client exceptions map to standard error codes", ErrorMappingExceptions);
            Add(cases, "tail-buffer", "Tail buffer keeps the end and counts the total", TailBuffer);
            Add(cases, "execute-rejected-offline", "Rejected calls fail without contacting the cluster", ExecuteRejectedOffline);
            Add(cases, "execute-readonly-offline", "A ReadOnly binding refuses changes without contacting the cluster", ExecuteReadOnlyOffline);
            Add(cases, "execute-secret-missing", "A tool without a bound credential returns secret_missing", ExecuteSecretMissing);
            Add(cases, "test-connection-failures", "TestConnectionAsync reports missing credentials and refused connections", TestConnectionFailures);
            Add(cases, "execute-cancelled", "A cancelled token throws OperationCanceledException", ExecuteCancelled);
            return cases;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
        }

        private static IConnectorTool Tool(string name)
        {
            IConnectorTool? tool = _Connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        private static EffectClassification Classify(string tool, object args, Dictionary<string, string>? settings = null)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("unit", settings ?? new Dictionary<string, string>(), null))
            {
                return Tool(tool).Classify(r.Invocation(), new ToolArguments(JsonSerializer.Serialize(args)));
            }
        }

        private static string Verdict(EffectClassification c)
        {
            return c.Rejected ? "X" : c.Effect == EffectEnum.Read ? "R" : c.Effect == EffectEnum.Write ? "W" : "D";
        }

        private static string Kubeconfig(string server = "https://127.0.0.1:1", string userBlock = "    token: abc123\n", string? context = "test", string clusterExtra = "")
        {
            return "apiVersion: v1\nkind: Config\n" + (context == null ? String.Empty : "current-context: " + context + "\n")
                + "clusters:\n- name: c1\n  cluster:\n    server: " + server + "\n    insecure-skip-tls-verify: true\n" + clusterExtra
                + "contexts:\n- name: test\n  context:\n    cluster: c1\n    user: u1\n    namespace: hugo-test\n- name: other\n  context:\n    cluster: c1\n    user: u1\n"
                + "users:\n- name: u1\n  user:\n" + userBlock;
        }

        private static Dictionary<string, Dictionary<string, string>> KubeconfigSecret(string text)
        {
            return new Dictionary<string, Dictionary<string, string>> { { "kubeconfig", new Dictionary<string, string> { { "kubeconfig", text } } } };
        }

        private static async Task<ConnectorValidationResult> Validate(Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets, CancellationToken token)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("validate", settings, secrets))
            {
                return await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
            }
        }

        private static string SelfSignedPem()
        {
            using (RSA rsa = RSA.Create(2048))
            {
                CertificateRequest req = new CertificateRequest("CN=hugo-test-ca", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using (X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
                {
                    return cert.ExportCertificatePem();
                }
            }
        }

        private static Task Descriptor(CancellationToken token)
        {
            ConnectorDescriptor d = _Connector.Descriptor;
            SshAssert.True(d.Type == "kubernetes", "type");
            SshAssert.True(d.Family == "k8s", "family");
            SshAssert.True(d.Category == ConnectorCategoryEnum.Orchestration, "category");
            SshAssert.True(d.IconKey == "cluster", "icon");
            SshAssert.True(d.DefaultInstructionsKey == "connector.kubernetes.instructions", "instructions key");
            SshAssert.Contains(d.DefaultInstructions, "k8s_describe", "instructions mention describe");
            SshAssert.Contains(d.DefaultInstructions, "previous: true", "instructions mention previous logs");
            SshAssert.NotContains(d.DefaultInstructions, "\u2014", "instructions (em-dash)");
            SshAssert.True(d.SecretSlots.Any(s => s.Name == "kubeconfig" && s.AcceptedKinds.Contains(SecretKindEnum.Kubeconfig)), "kubeconfig slot");
            SshAssert.True(d.SecretSlots.Any(s => s.Name == "serviceAccountToken" && s.AcceptedKinds.Contains(SecretKindEnum.ApiToken)), "token slot");
            SshAssert.True(d.SecretSlots.Any(s => s.Name == "certificateAuthority" && s.AcceptedKinds.Contains(SecretKindEnum.Certificate)), "CA slot");
            SshAssert.True(d.SecretSlots.All(s => !s.Required), "slots optional (cross-slot rules in ValidateAsync)");
            return Task.CompletedTask;
        }

        private static Task SettingsSchema(CancellationToken token)
        {
            ConnectorSettingsSchema s = _Connector.Descriptor.SettingsSchema;
            string[] keys = new[] { "context", "serverUrl", "allowedNamespaces", "defaultNamespace", "maxLogLines", "requestTimeoutSeconds", "maxObjects", "allowClusterScoped", "clusterScopedReads", "destructiveKinds", "execTimeoutSeconds", "insecureSkipTlsVerify", "fieldManager" };
            foreach (string key in keys) SshAssert.True(s.Find(key) != null, "setting " + key);
            SshAssert.True(s.Find("maxLogLines")!.DefaultValue == "500", "maxLogLines default 500");
            SshAssert.True(s.Find("requestTimeoutSeconds")!.DefaultValue == "30" && s.Find("requestTimeoutSeconds")!.Maximum == 600, "request timeout");
            SshAssert.True(s.Find("maxObjects")!.DefaultValue == "200", "maxObjects default");
            SshAssert.True(s.Find("allowClusterScoped")!.DefaultValue == "false", "allowClusterScoped default false");
            SshAssert.True(s.Find("clusterScopedReads")!.DefaultValue == "true", "clusterScopedReads default true");
            SshAssert.True(s.Find("allowedNamespaces")!.Kind == SettingValueKindEnum.StringList, "allowedNamespaces is a list");
            SshAssert.True(s.Settings.All(x => !String.IsNullOrEmpty(x.Help)), "every setting has help");
            return Task.CompletedTask;
        }

        private static Task ToolsShape(CancellationToken token)
        {
            Dictionary<string, EffectEnum> expected = new Dictionary<string, EffectEnum>
            {
                { "k8s_list", EffectEnum.Read },
                { "k8s_get", EffectEnum.Read },
                { "k8s_describe", EffectEnum.Read },
                { "k8s_events", EffectEnum.Read },
                { "k8s_logs", EffectEnum.Read },
                { "k8s_top", EffectEnum.Read },
                { "k8s_rollout_status", EffectEnum.Read },
                { "k8s_rollout_history", EffectEnum.Read },
                { "k8s_rollout_restart", EffectEnum.Write },
                { "k8s_scale", EffectEnum.Write },
                { "k8s_apply", EffectEnum.Write },
                { "k8s_delete", EffectEnum.Destructive },
                { "k8s_exec", EffectEnum.Destructive },
                { "k8s_cordon", EffectEnum.Destructive }
            };
            SshAssert.True(_Connector.Tools.Count == expected.Count, "tool count");
            foreach (KeyValuePair<string, EffectEnum> kv in expected)
            {
                ToolDescriptor d = Tool(kv.Key).Descriptor;
                SshAssert.True(d.BaseEffect == kv.Value, kv.Key + " base effect");
                SshAssert.True(d.Description.Length > 80, kv.Key + " description");
                SshAssert.True(!String.IsNullOrEmpty(d.Title), kv.Key + " title");
                SshAssert.True(d.Name.StartsWith("k8s_", StringComparison.Ordinal), kv.Key + " family prefix");
            }
            SshAssert.True(Tool("k8s_apply").Descriptor.ClassifiedPerCall, "apply per call");
            SshAssert.True(Tool("k8s_scale").Descriptor.ClassifiedPerCall, "scale per call");
            SshAssert.True(Tool("k8s_delete").Descriptor.ClassifiedPerCall, "delete per call");
            return Task.CompletedTask;
        }

        private static Task ToolSchemas(CancellationToken token)
        {
            foreach (IConnectorTool tool in _Connector.Tools)
            {
                SshSchemaShape? shape = JsonSerializer.Deserialize<SshSchemaShape>(tool.Descriptor.InputSchemaJson);
                SshAssert.True(shape != null && shape.Type == "object", tool.Descriptor.Name + " schema type");
                SshAssert.True(shape!.Properties != null, tool.Descriptor.Name + " properties");
                SshAssert.True(shape.AdditionalProperties == false, tool.Descriptor.Name + " additionalProperties false");
                SshAssert.True(!shape.Properties!.ContainsKey("resource"), tool.Descriptor.Name + " does not declare resource");
                foreach (KeyValuePair<string, SshSchemaProperty> p in shape.Properties!)
                {
                    SshAssert.True(!String.IsNullOrWhiteSpace(p.Value.Description) && p.Value.Description!.Length > 20, tool.Descriptor.Name + "." + p.Key + " description");
                    SshAssert.True(!String.IsNullOrEmpty(p.Value.Type), tool.Descriptor.Name + "." + p.Key + " type");
                }
                foreach (string required in shape.Required ?? new List<string>())
                    SshAssert.True(shape.Properties!.ContainsKey(required), tool.Descriptor.Name + " required " + required);
                SshAssert.NotContains(tool.Descriptor.InputSchemaJson + tool.Descriptor.Description, "\u2014", tool.Descriptor.Name + " em-dash");
            }
            return Task.CompletedTask;
        }

        private static Task OptionsDefaults(CancellationToken token)
        {
            KubernetesOptions d = KubernetesOptions.FromSettings(new ResourceSettings(null));
            SshAssert.True(d.AllowedNamespaces.Count == 0, "all namespaces by default");
            SshAssert.True(d.MaxLogLines == 500 && d.RequestTimeoutSeconds == 30 && d.MaxObjects == 200 && d.ExecTimeoutSeconds == 60, "numeric defaults");
            SshAssert.True(!d.AllowClusterScoped && d.ClusterScopedReads && !d.InsecureSkipTlsVerify, "boolean defaults");
            SshAssert.True(d.FieldManager == "hugo", "field manager");
            SshAssert.True(d.IsDestructiveKind("clusterrole") && d.IsDestructiveKind("Namespace") && !d.IsDestructiveKind("Deployment"), "default destructive kinds");

            KubernetesOptions o = KubernetesOptions.FromSettings(new ResourceSettings(new Dictionary<string, string>
            {
                { "allowedNamespaces", "shop\npayments, web  web" },
                { "maxLogLines", "999999" },
                { "requestTimeoutSeconds", "0" },
                { "maxObjects", "-5" },
                { "destructiveKinds", "Deployment" },
                { "context", "  prod  " },
                { "fieldManager", "  " }
            }));
            SshAssert.True(o.AllowedNamespaces.SequenceEqual(new[] { "shop", "payments", "web" }), "allowedNamespaces split and de-duplicated: " + String.Join("|", o.AllowedNamespaces));
            SshAssert.True(o.MaxLogLines == 100000 && o.RequestTimeoutSeconds == 1 && o.MaxObjects == 1, "clamped");
            SshAssert.True(o.IsDestructiveKind("deployment") && !o.IsDestructiveKind("Role"), "custom destructive kinds replace defaults");
            SshAssert.True(o.Context == "prod", "context trimmed");
            SshAssert.True(o.FieldManager == "hugo", "blank field manager falls back");
            return Task.CompletedTask;
        }

        private static async Task ValidateCredentials(CancellationToken token)
        {
            ConnectorValidationResult none = await Validate(new Dictionary<string, string>(), null, token).ConfigureAwait(false);
            SshAssert.True(none.Errors.Any(e => e.Field == "kubeconfig" && e.Code == "required"), "credential required");

            ConnectorValidationResult ok = await Validate(new Dictionary<string, string>(), KubeconfigSecret(Kubeconfig()), token).ConfigureAwait(false);
            SshAssert.True(ok.IsValid, "valid kubeconfig: " + String.Join("; ", ok.Errors.Select(e => e.Message)));

            Dictionary<string, Dictionary<string, string>> tokenOnly = new Dictionary<string, Dictionary<string, string>> { { "serviceAccountToken", new Dictionary<string, string> { { "token", "t0ken" } } } };
            ConnectorValidationResult noServer = await Validate(new Dictionary<string, string>(), tokenOnly, token).ConfigureAwait(false);
            SshAssert.True(noServer.Errors.Any(e => e.Field == "serverUrl" && e.Code == "required"), "token needs serverUrl");
            ConnectorValidationResult withServer = await Validate(new Dictionary<string, string> { { "serverUrl", "https://10.0.0.1:6443" } }, tokenOnly, token).ConfigureAwait(false);
            SshAssert.True(withServer.IsValid, "token with serverUrl: " + String.Join("; ", withServer.Errors.Select(e => e.Message)));
            SshAssert.True(withServer.Errors.All(e => !e.Message.Contains("t0ken", StringComparison.Ordinal)), "no token in messages");
        }

        private static async Task ValidateSettings(CancellationToken token)
        {
            Dictionary<string, Dictionary<string, string>> secrets = KubeconfigSecret(Kubeconfig());
            ConnectorValidationResult badNs = await Validate(new Dictionary<string, string> { { "allowedNamespaces", "Shop_Prod" } }, secrets, token).ConfigureAwait(false);
            SshAssert.True(badNs.Errors.Any(e => e.Field == "allowedNamespaces"), "invalid namespace name");
            ConnectorValidationResult badDefault = await Validate(new Dictionary<string, string> { { "allowedNamespaces", "shop" }, { "defaultNamespace", "payments" } }, secrets, token).ConfigureAwait(false);
            SshAssert.True(badDefault.Errors.Any(e => e.Field == "defaultNamespace" && e.Code == "invalid_value"), "default outside allow list");
            ConnectorValidationResult goodDefault = await Validate(new Dictionary<string, string> { { "allowedNamespaces", "shop\npayments" }, { "defaultNamespace", "payments" } }, secrets, token).ConfigureAwait(false);
            SshAssert.True(goodDefault.IsValid, "default inside allow list");
            ConnectorValidationResult badUrl = await Validate(new Dictionary<string, string> { { "serverUrl", "ftp://x" } }, secrets, token).ConfigureAwait(false);
            SshAssert.True(badUrl.Errors.Any(e => e.Field == "serverUrl" && e.Code == "invalid_format"), "server URL scheme");
            ConnectorValidationResult badRange = await Validate(new Dictionary<string, string> { { "maxLogLines", "0" }, { "requestTimeoutSeconds", "601" } }, secrets, token).ConfigureAwait(false);
            SshAssert.True(badRange.Errors.Count(e => e.Code == "out_of_range") == 2, "ranges");

            Dictionary<string, Dictionary<string, string>> badCa = KubeconfigSecret(Kubeconfig());
            badCa["certificateAuthority"] = new Dictionary<string, string> { { "certificate", "not a pem" } };
            ConnectorValidationResult ca = await Validate(new Dictionary<string, string>(), badCa, token).ConfigureAwait(false);
            SshAssert.True(ca.Errors.Any(e => e.Field == "certificateAuthority" && e.Code == "invalid_format"), "bad CA");
            Dictionary<string, Dictionary<string, string>> goodCa = KubeconfigSecret(Kubeconfig());
            goodCa["certificateAuthority"] = new Dictionary<string, string> { { "certificate", SelfSignedPem() } };
            ConnectorValidationResult caOk = await Validate(new Dictionary<string, string>(), goodCa, token).ConfigureAwait(false);
            SshAssert.True(caOk.IsValid, "good CA: " + String.Join("; ", caOk.Errors.Select(e => e.Message)));
        }

        private static Task KubeconfigSafety(CancellationToken token)
        {
            KubeconfigInspection exec = KubeconfigInspector.Inspect(Kubeconfig(userBlock: "    exec:\n      apiVersion: client.authentication.k8s.io/v1\n      command: /bin/sh\n      args: [\"-c\", \"curl evil\"]\n"), null);
            SshAssert.True(!exec.IsValid && exec.ErrorText().Contains("exec credential plugin", StringComparison.Ordinal), "exec refused: " + exec.ErrorText());
            KubeconfigInspection provider = KubeconfigInspector.Inspect(Kubeconfig(userBlock: "    auth-provider:\n      name: gcp\n"), null);
            SshAssert.True(!provider.IsValid && provider.ErrorText().Contains("auth-provider", StringComparison.Ordinal), "auth-provider refused");
            KubeconfigInspection files = KubeconfigInspector.Inspect(Kubeconfig(userBlock: "    client-certificate: /etc/passwd\n    client-key: /etc/shadow\n"), null);
            SshAssert.True(!files.IsValid && files.ErrorText().Contains("files", StringComparison.Ordinal), "client file refs refused");
            KubeconfigInspection caFile = KubeconfigInspector.Inspect(Kubeconfig(clusterExtra: "    certificate-authority: /etc/ssl/ca.pem\n"), null);
            SshAssert.True(!caFile.IsValid && caFile.ErrorText().Contains("certificate-authority", StringComparison.Ordinal), "CA file ref refused");
            KubeconfigInspection inline = KubeconfigInspector.Inspect(Kubeconfig(userBlock: "    client-certificate-data: QUJD\n    client-key-data: REVG\n"), null);
            SshAssert.True(inline.IsValid, "inline data accepted: " + inline.ErrorText());
            KubeconfigInspection garbage = KubeconfigInspector.Inspect("{{{{ not yaml", null);
            SshAssert.True(!garbage.IsValid, "garbage refused");
            KubeconfigInspection empty = KubeconfigInspector.Inspect("  ", null);
            SshAssert.True(!empty.IsValid && empty.ErrorText().Contains("empty", StringComparison.Ordinal), "empty refused");
            return Task.CompletedTask;
        }

        private static Task KubeconfigContexts(CancellationToken token)
        {
            KubeconfigInspection current = KubeconfigInspector.Inspect(Kubeconfig(), null);
            SshAssert.True(current.IsValid && current.ContextName == "test" && current.Namespace == "hugo-test" && current.Server == "https://127.0.0.1:1", "current context");
            SshAssert.True(current.Contexts.SequenceEqual(new[] { "test", "other" }), "context names");
            KubeconfigInspection chosen = KubeconfigInspector.Inspect(Kubeconfig(), "other");
            SshAssert.True(chosen.IsValid && chosen.ContextName == "other", "explicit context");
            KubeconfigInspection missing = KubeconfigInspector.Inspect(Kubeconfig(), "prod");
            SshAssert.True(!missing.IsValid && missing.ErrorText().Contains("Available contexts: test, other", StringComparison.Ordinal), "missing context lists choices: " + missing.ErrorText());
            KubeconfigInspection noCurrent = KubeconfigInspector.Inspect(Kubeconfig(context: null), null);
            SshAssert.True(!noCurrent.IsValid && noCurrent.ErrorText().Contains("current-context", StringComparison.Ordinal), "ambiguous without current-context");
            return Task.CompletedTask;
        }

        private static Task SessionConfiguration(CancellationToken token)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("cfg", new Dictionary<string, string> { { "context", "other" } }, KubeconfigSecret(Kubeconfig(server: "https://10.1.2.3:6443"))))
            {
                k8s.KubernetesClientConfiguration c = KubernetesSessionFactory.BuildConfiguration(r.Context, KubernetesOptions.FromSettings(r.Context.Settings));
                SshAssert.True(c.Host.StartsWith("https://10.1.2.3:6443", StringComparison.Ordinal), "host from kubeconfig: " + c.Host);
                SshAssert.True(c.AccessToken == "abc123", "token from kubeconfig");
            }
            using (KubernetesTestResource r = new KubernetesTestResource("cfg2", new Dictionary<string, string> { { "serverUrl", "https://10.9.9.9:6443" } }, KubeconfigSecret(Kubeconfig())))
            {
                k8s.KubernetesClientConfiguration c = KubernetesSessionFactory.BuildConfiguration(r.Context, KubernetesOptions.FromSettings(r.Context.Settings));
                SshAssert.True(c.Host.StartsWith("https://10.9.9.9:6443", StringComparison.Ordinal), "serverUrl overrides kubeconfig: " + c.Host);
            }
            Dictionary<string, Dictionary<string, string>> tokenSecrets = new Dictionary<string, Dictionary<string, string>>
            {
                { "serviceAccountToken", new Dictionary<string, string> { { "token", " sa-token \n" } } },
                { "certificateAuthority", new Dictionary<string, string> { { "certificate", SelfSignedPem() } } }
            };
            using (KubernetesTestResource r = new KubernetesTestResource("cfg3", new Dictionary<string, string> { { "serverUrl", "https://10.0.0.1:6443" } }, tokenSecrets))
            {
                k8s.KubernetesClientConfiguration c = KubernetesSessionFactory.BuildConfiguration(r.Context, KubernetesOptions.FromSettings(r.Context.Settings));
                SshAssert.True(c.AccessToken == "sa-token", "token trimmed");
                SshAssert.True(c.SslCaCerts != null && c.SslCaCerts.Count == 1, "CA loaded");
                SshAssert.True(!c.SkipTlsVerify, "TLS verified by default");
            }
            using (KubernetesTestResource r = new KubernetesTestResource("cfg4", new Dictionary<string, string>(), KubeconfigSecret(Kubeconfig(userBlock: "    exec:\n      command: /bin/true\n"))))
            {
                try
                {
                    KubernetesSessionFactory.BuildConfiguration(r.Context, KubernetesOptions.FromSettings(r.Context.Settings));
                    throw new InvalidOperationException("exec kubeconfig should be refused");
                }
                catch (ConnectorException e)
                {
                    SshAssert.True(e.Code == "invalid_setting", "invalid_setting: " + e.Code);
                }
            }
            return Task.CompletedTask;
        }

        private static Task NamespacePolicy(CancellationToken token)
        {
            KubernetesOptions open = new KubernetesOptions();
            KubernetesOptions shop = new KubernetesOptions { AllowedNamespaces = new List<string> { "shop", "payments" } };
            SshAssert.True(KubernetesNamespacePolicy.IsAllowed(open, "anything"), "open allows all");
            SshAssert.True(KubernetesNamespacePolicy.IsAllowed(shop, "payments") && !KubernetesNamespacePolicy.IsAllowed(shop, "kube-system"), "allow list");
            SshAssert.True(!KubernetesNamespacePolicy.IsAllowed(shop, "Shop"), "case-sensitive");
            SshAssert.True(KubernetesNamespacePolicy.Effective(open, null) == "default", "default namespace");
            SshAssert.True(KubernetesNamespacePolicy.Effective(shop, null) == "shop", "first allowed");
            SshAssert.True(KubernetesNamespacePolicy.Effective(new KubernetesOptions { DefaultNamespace = "payments", AllowedNamespaces = new List<string> { "shop", "payments" } }, " ") == "payments", "configured default");
            SshAssert.True(KubernetesNamespacePolicy.AllNamespaces(open) == null, "cluster-wide when open");
            SshAssert.True(KubernetesNamespacePolicy.AllNamespaces(shop)!.SequenceEqual(new[] { "shop", "payments" }), "each allowed namespace");
            try
            {
                KubernetesNamespacePolicy.Require(shop, "kube-system");
                throw new InvalidOperationException("Require should throw");
            }
            catch (ConnectorException e)
            {
                SshAssert.True(e.Code == "permission_denied", "permission_denied");
                SshAssert.Contains(e.Message, "shop, payments", "names the allow list");
            }
            KubernetesResourceType node = KubernetesKnownKinds.Find("node")!;
            SshAssert.True(KubernetesNamespacePolicy.ExplainClusterScoped(open, "Node", true) != null, "cluster writes off by default");
            SshAssert.True(KubernetesNamespacePolicy.ExplainClusterScoped(open, "Node", false) == null, "cluster reads on by default");
            SshAssert.True(KubernetesNamespacePolicy.ExplainClusterScoped(new KubernetesOptions { AllowClusterScoped = true }, "Node", true) == null, "allowClusterScoped");
            SshAssert.True(KubernetesNamespacePolicy.ExplainClusterScoped(new KubernetesOptions { ClusterScopedReads = false }, "Node", false) != null, "clusterScopedReads false");
            try
            {
                KubernetesNamespacePolicy.RequireClusterScoped(open, node, true);
                throw new InvalidOperationException("RequireClusterScoped should throw");
            }
            catch (ConnectorException e)
            {
                SshAssert.True(e.Code == "permission_denied", "cluster permission_denied");
            }
            return Task.CompletedTask;
        }

        private static Task KnownKinds(CancellationToken token)
        {
            SshAssert.True(KubernetesKnownKinds.Find("deploy")!.Kind == "Deployment", "short name");
            SshAssert.True(KubernetesKnownKinds.Find("Deployments")!.Group == "apps", "plural, case-insensitive");
            SshAssert.True(KubernetesKnownKinds.Find("po")!.Kind == "Pod", "pod short name");
            SshAssert.True(KubernetesKnownKinds.Find("crd")!.Kind == "CustomResourceDefinition", "crd");
            SshAssert.True(KubernetesKnownKinds.Find("deployments.apps")!.Kind == "Deployment", "plural.group");
            SshAssert.True(KubernetesKnownKinds.Find("Event", "events.k8s.io/v1")!.Group == "events.k8s.io", "apiVersion picks the group");
            SshAssert.True(KubernetesKnownKinds.Find("Event")!.Group == "", "core event by default");
            SshAssert.True(KubernetesKnownKinds.Find("HorizontalPodAutoscaler", "autoscaling/v1")!.Version == "v1", "apiVersion picks the version");
            SshAssert.True(KubernetesKnownKinds.Find("Widget") == null, "unknown kinds are not guessed");
            SshAssert.True(KubernetesKnownKinds.IsClusterScoped("ns") && KubernetesKnownKinds.IsClusterScoped("ClusterRole") && KubernetesKnownKinds.IsClusterScoped("pv"), "cluster-scoped kinds");
            SshAssert.True(!KubernetesKnownKinds.IsClusterScoped("Role") && !KubernetesKnownKinds.IsClusterScoped("Widget"), "namespaced and unknown kinds");
            SshAssert.True(KubernetesKnownKinds.GroupOf("apps/v1") == "apps" && KubernetesKnownKinds.GroupOf("v1") == "" && KubernetesKnownKinds.VersionOf("apps/v1") == "v1", "group and version");
            SshAssert.True(KubernetesKnownKinds.Find("deploy")!.Subresources.Contains("scale"), "deployments scale");
            return Task.CompletedTask;
        }

        private static Task ResourcePaths(CancellationToken token)
        {
            KubernetesResourceType pod = KubernetesKnownKinds.Find("Pod")!;
            KubernetesResourceType deploy = KubernetesKnownKinds.Find("Deployment")!;
            KubernetesResourceType node = KubernetesKnownKinds.Find("Node")!;
            SshAssert.True(pod.Path("hugo-test", null) == "/api/v1/namespaces/hugo-test/pods", "core collection");
            SshAssert.True(pod.Path(null, null) == "/api/v1/pods", "core all namespaces");
            SshAssert.True(deploy.Path("shop", "web") == "/apis/apps/v1/namespaces/shop/deployments/web", "grouped object");
            SshAssert.True(deploy.Path("shop", "web", "scale") == "/apis/apps/v1/namespaces/shop/deployments/web/scale", "subresource");
            SshAssert.True(node.Path("ignored", "n1") == "/api/v1/nodes/n1", "cluster-scoped ignores namespace");
            SshAssert.True(pod.Path("ns", "a b/c") == "/api/v1/namespaces/ns/pods/a%20b%2Fc", "names are escaped");
            SshAssert.True(deploy.ApiVersion == "apps/v1" && pod.ApiVersion == "v1" && deploy.DisplayName == "deployments.apps", "apiVersion and display name");
            SshAssert.True(KubernetesSession.Query("a", "1", "b", null, "c", "x y") == "?a=1&c=x%20y", "query builder");
            return Task.CompletedTask;
        }

        private static Task ClassifyReadTools(CancellationToken token)
        {
            Dictionary<string, string> restricted = new Dictionary<string, string> { { "allowedNamespaces", "hugo-test" } };
            List<KeyValuePair<string, object>> reads = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("k8s_list", new { kind = "Pod" }),
                new KeyValuePair<string, object>("k8s_get", new { kind = "Pod", name = "p" }),
                new KeyValuePair<string, object>("k8s_describe", new { kind = "Pod", name = "p" }),
                new KeyValuePair<string, object>("k8s_events", new { }),
                new KeyValuePair<string, object>("k8s_logs", new { pod = "p" }),
                new KeyValuePair<string, object>("k8s_top", new { }),
                new KeyValuePair<string, object>("k8s_rollout_status", new { name = "web" }),
                new KeyValuePair<string, object>("k8s_rollout_history", new { name = "web" })
            };
            foreach (KeyValuePair<string, object> call in reads)
            {
                EffectClassification c = Classify(call.Key, call.Value, restricted);
                SshAssert.True(Verdict(c) == "R", call.Key + " is Read: " + c.Reason);
                SshAssert.True(!String.IsNullOrEmpty(c.ApprovalSummary) && c.ApprovalSummary.Contains("unit", StringComparison.Ordinal), call.Key + " summary names the target: " + c.ApprovalSummary);
            }
            List<KeyValuePair<string, object>> outside = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("k8s_list", new { kind = "Pod", @namespace = "kube-system" }),
                new KeyValuePair<string, object>("k8s_get", new { kind = "Secret", name = "s", @namespace = "kube-system" }),
                new KeyValuePair<string, object>("k8s_get", new { kind = "Namespace", name = "kube-system" }),
                new KeyValuePair<string, object>("k8s_describe", new { kind = "ns", name = "kube-system" }),
                new KeyValuePair<string, object>("k8s_events", new { @namespace = "kube-system" }),
                new KeyValuePair<string, object>("k8s_logs", new { pod = "p", @namespace = "kube-system" }),
                new KeyValuePair<string, object>("k8s_top", new { @namespace = "kube-system" }),
                new KeyValuePair<string, object>("k8s_rollout_status", new { name = "web", @namespace = "kube-system" }),
                new KeyValuePair<string, object>("k8s_rollout_history", new { name = "web", @namespace = "kube-system" })
            };
            foreach (KeyValuePair<string, object> call in outside)
            {
                EffectClassification c = Classify(call.Key, call.Value, restricted);
                SshAssert.True(c.Rejected, call.Key + " outside the allow list is rejected");
                SshAssert.Contains(c.Reason, "allowed namespaces", call.Key + " reason");
            }
            SshAssert.True(Verdict(Classify("k8s_list", new { kind = "Pod", allNamespaces = true }, restricted)) == "R", "allNamespaces stays inside the list at execution");
            SshAssert.True(Verdict(Classify("k8s_list", new { kind = "Pod", @namespace = "kube-system" })) == "R", "open resource allows any namespace");
            return Task.CompletedTask;
        }

        private static Task ClassifyWriteTools(CancellationToken token)
        {
            EffectClassification restart = Classify("k8s_rollout_restart", new { name = "web", @namespace = "shop" });
            SshAssert.True(Verdict(restart) == "W", "restart W");
            SshAssert.Contains(restart.ApprovalSummary, "Deployment shop/web", "restart summary");
            SshAssert.True(Verdict(Classify("k8s_scale", new { name = "web", replicas = 3 })) == "W", "scale W");
            EffectClassification zero = Classify("k8s_scale", new { name = "web", replicas = 0 });
            SshAssert.True(Verdict(zero) == "D", "scale to zero D");
            SshAssert.Contains(zero.ApprovalSummary, "to 0 replica", "zero summary");
            SshAssert.True(Verdict(Classify("k8s_scale", new { name = "web" })) == "D", "scale without replicas fails closed");
            EffectClassification del = Classify("k8s_delete", new { kind = "Pod", name = "p1", @namespace = "shop" });
            SshAssert.True(Verdict(del) == "D" && !del.AlwaysRequireApproval, "delete pod D");
            SshAssert.Contains(del.ApprovalSummary, "Delete Pod shop/p1", "delete summary");
            SshAssert.True(Verdict(Classify("k8s_delete", new { kind = "Pod", name = "p1", dryRun = true })) == "R", "delete dry run R");
            EffectClassification exec = Classify("k8s_exec", new { pod = "p1", command = new[] { "cat", "/etc/hosts" }, container = "app" });
            SshAssert.True(Verdict(exec) == "D", "exec D");
            SshAssert.Contains(exec.ApprovalSummary, "Run \"cat /etc/hosts\" in pod default/p1 container app", "exec summary: " + exec.ApprovalSummary);
            SshAssert.True(Verdict(Classify("k8s_apply", new { manifest = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: a\n" })) == "W", "apply W");
            SshAssert.True(Verdict(Classify("k8s_apply", new { manifest = "garbage: [" })) == "D", "apply unparseable D");
            SshAssert.True(Verdict(Classify("k8s_scale", new { name = "web", replicas = 2, @namespace = "kube-system" }, new Dictionary<string, string> { { "allowedNamespaces", "shop" } })) == "X", "scale outside allow list");
            return Task.CompletedTask;
        }

        private static Task ClassifyClusterScoped(CancellationToken token)
        {
            Dictionary<string, string> allow = new Dictionary<string, string> { { "allowClusterScoped", "true" } };
            Dictionary<string, string> noReads = new Dictionary<string, string> { { "clusterScopedReads", "false" } };
            SshAssert.True(Verdict(Classify("k8s_cordon", new { node = "n1" })) == "X", "cordon rejected by default");
            SshAssert.True(Verdict(Classify("k8s_cordon", new { node = "n1", action = "uncordon" })) == "X", "uncordon rejected by default");
            EffectClassification cordon = Classify("k8s_cordon", new { node = "n1" }, allow);
            SshAssert.True(Verdict(cordon) == "D", "cordon D when allowed");
            SshAssert.Contains(cordon.ApprovalSummary, "Cordon node n1", "cordon summary");
            SshAssert.True(Verdict(Classify("k8s_delete", new { kind = "Namespace", name = "shop" })) == "X", "namespace delete rejected by default");
            EffectClassification nsDelete = Classify("k8s_delete", new { kind = "Namespace", name = "shop" }, allow);
            SshAssert.True(Verdict(nsDelete) == "D" && nsDelete.AlwaysRequireApproval, "namespace delete always needs approval");
            EffectClassification crdDelete = Classify("k8s_delete", new { kind = "crd", name = "widgets.example.com" }, allow);
            SshAssert.True(crdDelete.AlwaysRequireApproval, "CRD delete always needs approval");
            SshAssert.True(Verdict(Classify("k8s_delete", new { kind = "Namespace", name = "kube-system" }, new Dictionary<string, string> { { "allowClusterScoped", "true" }, { "allowedNamespaces", "shop" } })) == "X", "namespace outside allow list");
            SshAssert.True(Verdict(Classify("k8s_list", new { kind = "Node" })) == "R", "node reads allowed");
            SshAssert.True(Verdict(Classify("k8s_list", new { kind = "Node" }, noReads)) == "X", "node reads off");
            SshAssert.True(Verdict(Classify("k8s_top", new { target = "nodes" }, noReads)) == "X", "top nodes off");
            SshAssert.True(Verdict(Classify("k8s_list", new { kind = "Pod" }, noReads)) == "R", "namespaced reads unaffected");
            return Task.CompletedTask;
        }

        private static JsonNode SecretJson()
        {
            string value = Convert.ToBase64String(Encoding.UTF8.GetBytes("super-secret-password"));
            return JsonNode.Parse("{\"apiVersion\":\"v1\",\"kind\":\"Secret\",\"metadata\":{\"name\":\"s\",\"namespace\":\"shop\",\"managedFields\":[{\"manager\":\"kubectl\"}],"
                + "\"annotations\":{\"kubectl.kubernetes.io/last-applied-configuration\":\"{\\\"data\\\":{\\\"password\\\":\\\"" + value + "\\\"}}\",\"team\":\"core\"}},"
                + "\"type\":\"Opaque\",\"data\":{\"password\":\"" + value + "\",\"empty\":\"\"},\"stringData\":{\"token\":\"plain-token-value\"}}")!;
        }

        private static Task SecretRedaction(CancellationToken token)
        {
            JsonNode secret = SecretJson();
            KubernetesObjectSanitizer.Sanitize(secret, null);
            string text = secret.ToJsonString(KubernetesFormat.Indented).Replace("\": \"", "\":\"", StringComparison.Ordinal);
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("super-secret-password"));
            SshAssert.NotContains(text, encoded, "base64 value");
            SshAssert.NotContains(text, "plain-token-value", "stringData value");
            SshAssert.NotContains(text, "managedFields", "managedFields stripped");
            SshAssert.Contains(text, "<redacted: 21 bytes>", "size marker");
            SshAssert.Contains(text, "<redacted: 0 bytes>", "empty marker");
            SshAssert.Contains(text, "<redacted: contains secret values>", "last-applied redacted");
            SshAssert.Contains(text, "\"team\":\"core\"", "other annotations kept");
            SshAssert.True(!KubernetesObjectSanitizer.HasSecretValues(secret), "no secret values remain");
            SshAssert.True(KubernetesObjectSanitizer.HasSecretValues(SecretJson()), "detector sees raw values");
            string yaml = KubernetesYamlWriter.Write(secret);
            SshAssert.NotContains(yaml, encoded, "yaml output");
            JsonNode configMap = JsonNode.Parse("{\"kind\":\"ConfigMap\",\"metadata\":{\"name\":\"c\",\"managedFields\":[]},\"data\":{\"password\":\"visible\"}}")!;
            KubernetesObjectSanitizer.Sanitize(configMap, null);
            SshAssert.Contains(configMap.ToJsonString(), "visible", "configmaps are not secrets");
            SshAssert.NotContains(configMap.ToJsonString(), "managedFields", "configmap managedFields stripped");
            return Task.CompletedTask;
        }

        private static Task SecretRedactionLists(CancellationToken token)
        {
            string value = Convert.ToBase64String(Encoding.UTF8.GetBytes("list-secret"));
            JsonNode list = JsonNode.Parse("{\"kind\":\"SecretList\",\"items\":[{\"metadata\":{\"name\":\"a\"},\"data\":{\"k\":\"" + value + "\"}},{\"metadata\":{\"name\":\"b\"},\"stringData\":{\"k\":\"list-plain\"}}]}")!;
            KubernetesObjectSanitizer.Sanitize(list, null);
            SshAssert.NotContains(list.ToJsonString(), value, "SecretList items (no kind) redacted");
            SshAssert.NotContains(list.ToJsonString(), "list-plain", "SecretList stringData redacted");
            JsonNode generic = JsonNode.Parse("{\"kind\":\"List\",\"items\":[{\"kind\":\"Secret\",\"metadata\":{\"name\":\"a\"},\"data\":{\"k\":\"" + value + "\"}},{\"kind\":\"ConfigMap\",\"data\":{\"k\":\"keep\"}}]}")!;
            KubernetesObjectSanitizer.Sanitize(generic, null);
            SshAssert.NotContains(generic.ToJsonString(), value, "List item Secret redacted");
            SshAssert.Contains(generic.ToJsonString(), "keep", "List item ConfigMap kept");
            JsonNode hinted = JsonNode.Parse("{\"items\":[{\"metadata\":{\"name\":\"a\"},\"data\":{\"k\":\"" + value + "\"}}]}")!;
            KubernetesObjectSanitizer.Sanitize(hinted, "Secret");
            SshAssert.NotContains(hinted.ToJsonString(), value, "kind hint applies to items");
            JsonNode nested = JsonNode.Parse("{\"kind\":\"ControllerRevision\",\"data\":{\"embedded\":{\"kind\":\"Secret\",\"data\":{\"k\":\"" + value + "\"}}}}")!;
            KubernetesObjectSanitizer.Sanitize(nested, null);
            SshAssert.NotContains(nested.ToJsonString(), value, "nested Secret redacted");
            SshAssert.True(KubernetesObjectSanitizer.Marker(value) == "<redacted: 11 bytes>", "marker size");
            SshAssert.True(KubernetesObjectSanitizer.Marker("%%%not-base64") == "<redacted: 13 bytes>", "marker for invalid base64");
            return Task.CompletedTask;
        }

        private static Task DescribeSecretOffline(CancellationToken token)
        {
            string text = KubernetesDescriber.Describe(KubernetesKnownKinds.Find("Secret")!, SecretJson().ToJsonString(), DateTime.UtcNow);
            SshAssert.Contains(text, "password: 21 bytes", "key sizes");
            SshAssert.NotContains(text, "super-secret-password", "plain");
            SshAssert.NotContains(text, Convert.ToBase64String(Encoding.UTF8.GetBytes("super-secret-password")), "base64");
            SshAssert.NotContains(text, "last-applied", "last-applied annotation hidden");
            string pod = "{\"apiVersion\":\"v1\",\"kind\":\"Pod\",\"metadata\":{\"name\":\"p\",\"namespace\":\"shop\"},\"spec\":{\"containers\":[{\"name\":\"app\",\"image\":\"busybox\"}]},"
                + "\"status\":{\"phase\":\"Running\",\"containerStatuses\":[{\"name\":\"app\",\"ready\":false,\"restartCount\":4,\"image\":\"busybox\",\"imageID\":\"x\",\"state\":{\"waiting\":{\"reason\":\"CrashLoopBackOff\"}},\"lastState\":{\"terminated\":{\"reason\":\"Error\",\"exitCode\":1}}}]}}";
            string describedPod = KubernetesDescriber.Describe(KubernetesKnownKinds.Find("Pod")!, pod, DateTime.UtcNow);
            SshAssert.Contains(describedPod, "Waiting: CrashLoopBackOff", "waiting state");
            SshAssert.Contains(describedPod, "Terminated: Error, exit code 1", "last state");
            SshAssert.Contains(describedPod, "Restart Count:  4", "restarts");
            return Task.CompletedTask;
        }

        private static Task ManifestParser(CancellationToken token)
        {
            List<JsonObject> docs = KubernetesManifestParser.Parse("---\napiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: a\ndata:\n  n: 5\n  q: \"5\"\n  b: true\n  s: yes\n  f: 1.5\n---\n# comment only\n---\napiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: b\n");
            SshAssert.True(docs.Count == 2, "two documents, empty skipped");
            JsonObject data = (JsonObject)docs[0]["data"]!;
            SshAssert.True(data["n"]!.GetValueKind() == JsonValueKind.Number, "plain integer");
            SshAssert.True(data["q"]!.GetValueKind() == JsonValueKind.String, "quoted stays string");
            SshAssert.True(data["b"]!.GetValueKind() == JsonValueKind.True, "boolean");
            SshAssert.True(data["s"]!.GetValueKind() == JsonValueKind.String, "yes stays a string (YAML 1.2)");
            SshAssert.True(data["f"]!.GetValueKind() == JsonValueKind.Number, "float");
            List<JsonObject> json = KubernetesManifestParser.Parse("[{\"apiVersion\":\"v1\",\"kind\":\"ConfigMap\",\"metadata\":{\"name\":\"a\"}},{\"apiVersion\":\"v1\",\"kind\":\"ConfigMap\",\"metadata\":{\"name\":\"b\"}}]");
            SshAssert.True(json.Count == 2, "JSON array");
            List<JsonObject> list = KubernetesManifestParser.Parse("apiVersion: v1\nkind: List\nitems:\n- apiVersion: v1\n  kind: ConfigMap\n  metadata:\n    name: a\n");
            SshAssert.True(list.Count == 1 && KubernetesManifestParser.StringOf(list[0]["kind"]) == "ConfigMap", "List expanded");
            SshAssert.True(KubernetesManifestParser.ContainsNull(JsonNode.Parse("{\"a\":{\"b\":[1,null]}}")), "null in array");
            SshAssert.True(!KubernetesManifestParser.ContainsNull(JsonNode.Parse("{\"a\":{\"b\":[1,\"null\"]}}")), "string null");
            return Task.CompletedTask;
        }

        private static Task ManifestParserErrors(CancellationToken token)
        {
            string[] bad = new[]
            {
                "",
                "apiVersion: v1\nkind: ConfigMap\nmetadata: {}\n",
                "kind: ConfigMap\nmetadata:\n  name: a\n",
                "apiVersion: v1\nmetadata:\n  name: a\n",
                "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: a\ndata:\n  <<: {x: 1}\n",
                "- just\n- a\n- list\n",
                "{ not json",
                "key: [unclosed"
            };
            foreach (string manifest in bad)
            {
                try
                {
                    KubernetesManifestParser.Parse(manifest);
                    throw new InvalidOperationException("Expected a parse error for: " + manifest);
                }
                catch (ConnectorException e)
                {
                    SshAssert.True(e.Code == "invalid_argument", "invalid_argument for: " + manifest.Replace("\n", "\\n", StringComparison.Ordinal));
                }
            }
            StringBuilder many = new StringBuilder();
            for (int i = 0; i < 51; i++) many.Append("---\napiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: c").Append(i).Append('\n');
            try
            {
                KubernetesManifestParser.Parse(many.ToString());
                throw new InvalidOperationException("51 objects should be refused");
            }
            catch (ConnectorException e)
            {
                SshAssert.True(e.Code == "invalid_argument" && e.Message.Contains("at most 50", StringComparison.Ordinal), "too many objects");
            }
            return Task.CompletedTask;
        }

        private static Task YamlWriter(CancellationToken token)
        {
            JsonObject obj = new JsonObject
            {
                ["apiVersion"] = "v1",
                ["kind"] = "ConfigMap",
                ["metadata"] = new JsonObject { ["name"] = "tricky", ["labels"] = new JsonObject { ["app.kubernetes.io/name"] = "web" } },
                ["data"] = new JsonObject
                {
                    ["bool"] = "true",
                    ["number"] = "0123",
                    ["colon"] = "a: b",
                    ["hash"] = "x #y",
                    ["empty"] = "",
                    ["dash"] = "- item",
                    ["multi"] = "line1\nline2\n",
                    ["multinoend"] = "line1\nline2",
                    ["quote"] = "say \"hi\"",
                    ["star"] = "*alias",
                    ["date"] = "2024-01-01",
                    ["unicode"] = "café",
                    ["null"] = "null",
                    ["tilde"] = "~",
                    ["spaces"] = " padded "
                },
                ["spec"] = new JsonObject
                {
                    ["replicas"] = 3,
                    ["enabled"] = false,
                    ["ratio"] = 0.5,
                    ["nothing"] = null,
                    ["emptyList"] = new JsonArray(),
                    ["emptyMap"] = new JsonObject(),
                    ["ports"] = new JsonArray(new JsonObject { ["name"] = "http", ["port"] = 80 }, new JsonObject { ["name"] = "https", ["port"] = 443 }),
                    ["args"] = new JsonArray("-v", "--flag=x", "plain"),
                    ["nested"] = new JsonArray(new JsonArray("a", "b"))
                }
            };
            string yaml = KubernetesYamlWriter.Write(obj);
            SshAssert.Contains(yaml, "kind: ConfigMap", "plain scalars");
            SshAssert.Contains(yaml, "bool: \"true\"", "boolean-looking string quoted");
            SshAssert.Contains(yaml, "number: \"0123\"", "number-looking string quoted");
            SshAssert.Contains(yaml, "multi: |\n    line1\n    line2\n", "literal block");
            SshAssert.Contains(yaml, "  ports:\n  - name: http\n    port: 80\n", "sequence of maps");
            List<JsonObject> back = KubernetesManifestParser.Parse(yaml);
            SshAssert.True(back.Count == 1, "parses back");
            SshAssert.True(JsonNode.DeepEquals(back[0], obj), "round trip:\n" + yaml + "\n" + back[0].ToJsonString());
            return Task.CompletedTask;
        }

        private static Task FieldDiff(CancellationToken token)
        {
            JsonNode live = JsonNode.Parse("{\"metadata\":{\"name\":\"a\",\"resourceVersion\":\"1\",\"generation\":2,\"managedFields\":[1],\"annotations\":{\"deployment.kubernetes.io/revision\":\"3\",\"team\":\"x\"}},\"spec\":{\"replicas\":2,\"template\":{\"spec\":{\"containers\":[{\"name\":\"a\",\"ports\":[{\"containerPort\":80}]},{\"name\":\"b\"}]}}},\"status\":{\"replicas\":2}}")!;
            JsonNode same = live.DeepClone();
            same["metadata"]!["resourceVersion"] = "2";
            ((JsonObject)same).Remove("status");
            SshAssert.True(KubernetesFieldDiff.RemovedPaths(live, same).Count == 0, "status and server metadata ignored");
            JsonNode removed = live.DeepClone();
            ((JsonObject)removed["spec"]!["template"]!["spec"]!["containers"]![0]!).Remove("ports");
            ((JsonArray)removed["spec"]!["template"]!["spec"]!["containers"]!).RemoveAt(1);
            ((JsonObject)removed["metadata"]!["annotations"]!).Remove("team");
            List<string> paths = KubernetesFieldDiff.RemovedPaths(live, removed);
            SshAssert.True(paths.Contains("spec.template.spec.containers[0].ports"), "nested field: " + String.Join(", ", paths));
            SshAssert.True(paths.Contains("spec.template.spec.containers[1]"), "list element");
            SshAssert.True(paths.Contains("metadata.annotations.team"), "annotation");
            SshAssert.True(paths.Count == 3, "exactly three: " + String.Join(", ", paths));
            SshAssert.True(KubernetesFieldDiff.RemovedPaths(live, removed, 1).Count == 1, "max respected");
            return Task.CompletedTask;
        }

        private static Task RolloutRules(CancellationToken token)
        {
            V1Deployment d = new V1Deployment
            {
                Metadata = new V1ObjectMeta { Name = "web", Generation = 2 },
                Spec = new V1DeploymentSpec { Replicas = 3 },
                Status = new V1DeploymentStatus { ObservedGeneration = 1 }
            };
            SshAssert.Contains(KubernetesRollout.Evaluate(d).Message, "spec update to be observed", "not observed");
            d.Status = new V1DeploymentStatus { ObservedGeneration = 2, UpdatedReplicas = 1, Replicas = 3, AvailableReplicas = 3 };
            SshAssert.Contains(KubernetesRollout.Evaluate(d).Message, "1 out of 3 new replicas", "updating");
            d.Status = new V1DeploymentStatus { ObservedGeneration = 2, UpdatedReplicas = 3, Replicas = 4, AvailableReplicas = 3 };
            SshAssert.Contains(KubernetesRollout.Evaluate(d).Message, "old replicas are pending termination", "old pending");
            d.Status = new V1DeploymentStatus { ObservedGeneration = 2, UpdatedReplicas = 3, Replicas = 3, AvailableReplicas = 2 };
            SshAssert.Contains(KubernetesRollout.Evaluate(d).Message, "2 of 3 updated replicas are available", "availability");
            d.Status = new V1DeploymentStatus { ObservedGeneration = 2, UpdatedReplicas = 3, Replicas = 3, AvailableReplicas = 3 };
            KubernetesRolloutState done = KubernetesRollout.Evaluate(d);
            SshAssert.True(done.Done && !done.Failed, "done");
            d.Status = new V1DeploymentStatus { ObservedGeneration = 2, Conditions = new List<V1DeploymentCondition> { new V1DeploymentCondition { Type = "Progressing", Status = "False", Reason = "ProgressDeadlineExceeded" } } };
            SshAssert.True(KubernetesRollout.Evaluate(d).Failed, "deadline exceeded");

            V1StatefulSet s = new V1StatefulSet { Metadata = new V1ObjectMeta { Name = "db", Generation = 1 }, Spec = new V1StatefulSetSpec { Replicas = 2 }, Status = new V1StatefulSetStatus { ObservedGeneration = 1, ReadyReplicas = 1, CurrentRevision = "r1", UpdateRevision = "r1" } };
            SshAssert.Contains(KubernetesRollout.Evaluate(s).Message, "Waiting for 1 pods to be ready", "sts not ready");
            s.Status.ReadyReplicas = 2;
            s.Status.UpdateRevision = "r2";
            SshAssert.True(!KubernetesRollout.Evaluate(s).Done, "sts revision mismatch");
            s.Status.CurrentRevision = "r2";
            SshAssert.True(KubernetesRollout.Evaluate(s).Done, "sts done");

            V1DaemonSet ds = new V1DaemonSet { Metadata = new V1ObjectMeta { Name = "agent", Generation = 1 }, Status = new V1DaemonSetStatus { ObservedGeneration = 1, DesiredNumberScheduled = 3, UpdatedNumberScheduled = 3, NumberAvailable = 2 } };
            SshAssert.Contains(KubernetesRollout.Evaluate(ds).Message, "2 of 3 updated pods are available", "ds availability");
            ds.Status.NumberAvailable = 3;
            SshAssert.True(KubernetesRollout.Evaluate(ds).Done, "ds done");

            SshAssert.True(KubernetesRollout.Kind(null).Kind == "Deployment" && KubernetesRollout.Kind("sts").Kind == "StatefulSet", "rollout kinds");
            try
            {
                KubernetesRollout.Kind("Pod");
                throw new InvalidOperationException("Pod is not a rollout kind");
            }
            catch (ConnectorException e)
            {
                SshAssert.True(e.Code == "invalid_argument", "invalid rollout kind");
            }
            return Task.CompletedTask;
        }

        private static Task ErrorMappingStatus(CancellationToken token)
        {
            string forbidden = "{\"kind\":\"Status\",\"apiVersion\":\"v1\",\"status\":\"Failure\",\"message\":\"secrets is forbidden: User cannot list resource\",\"reason\":\"Forbidden\",\"code\":403}";
            ConnectorException e403 = KubernetesErrorMapper.FromStatus(HttpStatusCode.Forbidden, forbidden, "secrets in namespace shop");
            SshAssert.True(e403.Code == "permission_denied", "403");
            SshAssert.Contains(e403.Message, "secrets is forbidden", "server message kept");
            SshAssert.True(KubernetesErrorMapper.FromStatus(HttpStatusCode.Unauthorized, null, "x").Code == "auth_failed", "401");
            SshAssert.True(KubernetesErrorMapper.FromStatus(HttpStatusCode.NotFound, null, "pod shop/p").Code == "not_found", "404");
            string exists = "{\"kind\":\"Status\",\"reason\":\"AlreadyExists\",\"message\":\"exists\"}";
            SshAssert.True(KubernetesErrorMapper.FromStatus(HttpStatusCode.Conflict, exists, "x").Code == "already_exists", "409 exists");
            ConnectorException conflict = KubernetesErrorMapper.FromStatus(HttpStatusCode.Conflict, "{\"kind\":\"Status\",\"reason\":\"Conflict\",\"message\":\"Apply failed with 1 conflict\"}", "x");
            SshAssert.True(conflict.Code == "invalid_argument" && conflict.Message.Contains("force", StringComparison.Ordinal), "409 conflict suggests force");
            SshAssert.True(KubernetesErrorMapper.FromStatus((HttpStatusCode)422, null, "x").Code == "invalid_argument", "422");
            SshAssert.True(KubernetesErrorMapper.FromStatus(HttpStatusCode.BadRequest, null, "x").Code == "invalid_argument", "400");
            ConnectorException limited = KubernetesErrorMapper.FromStatus((HttpStatusCode)429, null, "x", 7);
            SshAssert.True(limited.Code == "rate_limited" && limited.RetryAfterSeconds == 7, "429 with retry");
            SshAssert.True(KubernetesErrorMapper.FromStatus(HttpStatusCode.GatewayTimeout, null, "x").Code == "timeout", "504");
            SshAssert.True(KubernetesErrorMapper.FromStatus(HttpStatusCode.InternalServerError, null, "x").Code == "upstream_error", "500");
            SshAssert.True(KubernetesErrorMapper.FromStatus(HttpStatusCode.Gone, null, "x").Code == "invalid_argument", "410 expired continue");
            SshAssert.True(KubernetesErrorMapper.ParseStatus("not json") == null && KubernetesErrorMapper.ParseStatus("{\"kind\":\"Pod\"}") == null, "non-status bodies");
            return Task.CompletedTask;
        }

        private static Task ErrorMappingExceptions(CancellationToken token)
        {
            SshAssert.True(KubernetesErrorMapper.FromException(new HttpRequestException("Connection refused"), "x", "https://10.0.0.1").Code == "connection_failed", "network");
            SshAssert.True(KubernetesErrorMapper.FromException(new HttpRequestException("tls", new AuthenticationException("bad cert")), "x", null).Code == "connection_failed", "tls");
            SshAssert.True(KubernetesErrorMapper.FromException(new TaskCanceledException(), "x", null).Code == "timeout", "client timeout");
            SshAssert.True(KubernetesErrorMapper.FromException(new TimeoutException(), "x", null).Code == "timeout", "timeout");
            SshAssert.True(KubernetesErrorMapper.FromException(new k8s.Exceptions.KubeConfigException("bad"), "x", null).Code == "invalid_setting", "kubeconfig");
            ConnectorException passthrough = new ConnectorException("not_found", "kept");
            SshAssert.True(Object.ReferenceEquals(KubernetesErrorMapper.FromException(passthrough, "x", null), passthrough), "connector exceptions pass through");
            SshAssert.True(KubernetesErrorMapper.FromException(new InvalidOperationException("boom"), "x", null).Code == "upstream_error", "other");
            return Task.CompletedTask;
        }

        private static Task TailBuffer(CancellationToken token)
        {
            KubernetesTailBuffer b = new KubernetesTailBuffer(8);
            byte[] data = Encoding.UTF8.GetBytes("0123456789");
            b.Write(data, 0, 4);
            SshAssert.True(b.Text() == "0123" && !b.Dropped, "under capacity");
            b.Write(data, 4, 6);
            SshAssert.True(b.Text() == "23456789" && b.Dropped && b.TotalBytes == 10, "keeps the end: " + b.Text());
            b.Write(Encoding.UTF8.GetBytes("abcdefghijkl"), 0, 12);
            SshAssert.True(b.Text() == "efghijkl" && b.TotalBytes == 22, "large write");
            KubernetesTailBuffer utf = new KubernetesTailBuffer(5);
            byte[] cafe = Encoding.UTF8.GetBytes("xxéé");
            utf.Write(cafe, 0, cafe.Length);
            SshAssert.True(!utf.Text().Contains('�', StringComparison.Ordinal), "no broken leading character");
            return Task.CompletedTask;
        }

        private static async Task ExecuteRejectedOffline(CancellationToken token)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("offline", new Dictionary<string, string> { { "allowedNamespaces", "shop" } }, null))
            {
                ToolResult result = await Tool("k8s_get").ExecuteAsync(r.Invocation(), new ToolArguments("{\"kind\":\"Secret\",\"name\":\"s\",\"namespace\":\"kube-system\"}"), token).ConfigureAwait(false);
                SshAssert.Error(result, "rejected", "rejected before connecting (no credential bound, so connecting would say secret_missing)");
                ToolResult cordon = await Tool("k8s_cordon").ExecuteAsync(r.Invocation(), new ToolArguments("{\"node\":\"n1\"}"), token).ConfigureAwait(false);
                SshAssert.Error(cordon, "rejected", "cordon rejected");
            }
        }

        private static async Task ExecuteReadOnlyOffline(CancellationToken token)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("offline-ro", new Dictionary<string, string>(), null))
            {
                ConnectorInvocationContext ctx = r.Invocation(AccessModeEnum.ReadOnly);
                ToolResult scale = await Tool("k8s_scale").ExecuteAsync(ctx, new ToolArguments("{\"name\":\"web\",\"replicas\":2}"), token).ConfigureAwait(false);
                SshAssert.Error(scale, "permission_denied", "scale");
                ToolResult exec = await Tool("k8s_exec").ExecuteAsync(ctx, new ToolArguments("{\"pod\":\"p\",\"command\":[\"id\"]}"), token).ConfigureAwait(false);
                SshAssert.Error(exec, "permission_denied", "exec");
            }
        }

        private static async Task ExecuteSecretMissing(CancellationToken token)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("nocred", new Dictionary<string, string>(), null))
            {
                ToolResult result = await Tool("k8s_list").ExecuteAsync(r.Invocation(), new ToolArguments("{\"kind\":\"Pod\"}"), token).ConfigureAwait(false);
                SshAssert.Error(result, "secret_missing", "list without credential");
            }
            using (KubernetesTestResource r = new KubernetesTestResource("execcred", new Dictionary<string, string>(), KubeconfigSecret(Kubeconfig(userBlock: "    exec:\n      command: /bin/true\n"))))
            {
                ToolResult result = await Tool("k8s_list").ExecuteAsync(r.Invocation(), new ToolArguments("{\"kind\":\"Pod\"}"), token).ConfigureAwait(false);
                SshAssert.Error(result, "invalid_setting", "exec kubeconfig refused at run time");
            }
        }

        private static async Task TestConnectionFailures(CancellationToken token)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("hc-nocred", new Dictionary<string, string>(), null))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "secret_missing", "secret_missing: " + h.ErrorCode);
            }
            using (KubernetesTestResource r = new KubernetesTestResource("hc-refused", new Dictionary<string, string> { { "requestTimeoutSeconds", "5" } }, KubeconfigSecret(Kubeconfig(server: "https://127.0.0.1:1"))))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && (h.ErrorCode == "connection_failed" || h.ErrorCode == "timeout"), "refused: " + h.ErrorCode + " " + h.Message);
                SshAssert.NotContains(h.Message, "abc123", "token not in message");
                ToolResult list = await Tool("k8s_list").ExecuteAsync(r.Invocation(), new ToolArguments("{\"kind\":\"Pod\"}"), token).ConfigureAwait(false);
                SshAssert.True(list.IsError && (list.ErrorCode == "connection_failed" || list.ErrorCode == "timeout"), "tool maps refusal: " + list.ErrorCode);
            }
        }

        private static async Task ExecuteCancelled(CancellationToken token)
        {
            using (KubernetesTestResource r = new KubernetesTestResource("cancel", new Dictionary<string, string>(), KubeconfigSecret(Kubeconfig())))
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool thrown = false;
                try
                {
                    await Tool("k8s_list").ExecuteAsync(r.Invocation(), new ToolArguments("{\"kind\":\"Pod\"}"), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    thrown = true;
                }
                SshAssert.True(thrown, "OperationCanceledException");
            }
        }

        #endregion
    }
}
