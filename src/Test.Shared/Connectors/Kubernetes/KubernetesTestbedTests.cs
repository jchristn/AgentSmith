namespace Test.Shared.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Kubernetes;
    using Test.Shared.Connectors.Ssh;
    using Touchstone.Core;

    /// <summary>
    /// Kubernetes connector integration cases against the k3s testbed (<c>docker/testbed/kubernetes/compose.yaml</c>).
    /// Every tool is exercised. Cases are skipped with a reason when the testbed is not enabled or not reachable.
    /// Objects the cases create carry a unique suffix and are deleted afterwards.
    /// </summary>
    public static class KubernetesTestbedTests
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "testbed.kubernetes";

        #endregion

        #region Private-Members

        private static readonly KubernetesConnector _Connector = new KubernetesConnector();
        private static readonly string _Run = Guid.NewGuid().ToString("N").Substring(0, 6);
        private static readonly TimeSpan _CrasherTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan _PollInterval = TimeSpan.FromSeconds(2);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the testbed suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "test-connection-kubeconfig", "Test connection with the kubeconfig reports the server version", TestConnectionKubeconfig);
            Add(cases, "test-connection-token", "Test connection with a service account token and CA", TestConnectionToken);
            Add(cases, "token-rbac-denied", "The read-only token maps RBAC refusals to permission_denied", TokenRbacDenied);
            Add(cases, "list-pods", "k8s_list prints pods with kubectl columns", ListPods);
            Add(cases, "list-selector-paging", "k8s_list filters by label and pages with continue", ListSelectorPaging);
            Add(cases, "list-crd", "k8s_list lists custom resources with their printer columns", ListCustomResources);
            Add(cases, "list-namespaces-filtered", "k8s_list of namespaces and allNamespaces stay inside the allow list", ListNamespacesFiltered);
            Add(cases, "list-cluster-scoped-reads", "clusterScopedReads false refuses nodes", ListClusterScopedReads);
            Add(cases, "namespace-enforcement", "Every tool refuses a namespace outside the allow list", NamespaceEnforcement);
            Add(cases, "get-yaml", "k8s_get returns YAML without managedFields", GetYaml);
            Add(cases, "get-secret-redacted", "k8s_get, k8s_describe, and k8s_list never return Secret values", GetSecretRedacted);
            Add(cases, "get-not-found", "k8s_get maps a missing object to not_found", GetNotFound);
            Add(cases, "describe", "k8s_describe summarizes pods, deployments, services, and nodes with events", Describe);
            Add(cases, "events", "k8s_events lists Warning events for the crash-looping pod", Events);
            Add(cases, "logs", "k8s_logs reads the current and previous container logs", Logs);
            Add(cases, "top-without-metrics-server", "k8s_top reports not_supported without metrics-server", TopWithoutMetrics);
            Add(cases, "rollout-status-history", "k8s_rollout_status and k8s_rollout_history report the fixture deployment", RolloutStatusHistory);
            Add(cases, "apply-lifecycle", "k8s_apply dry run, create, unchanged, configure, field removal guard", ApplyLifecycle);
            Add(cases, "apply-crd-and-role", "k8s_apply handles custom resources and destructive kinds", ApplyCustomAndRole);
            Add(cases, "apply-field-manager", "k8s_apply applies as the resource's fieldManager, so two managers conflict", ApplyFieldManager);
            Add(cases, "rollout-restart", "k8s_rollout_restart restarts a deployment and the rollout completes", RolloutRestart);
            Add(cases, "scale", "k8s_scale changes replicas and scaling to zero is Destructive", Scale);
            Add(cases, "delete", "k8s_delete dry run, delete, and not_found", Delete);
            Add(cases, "exec", "k8s_exec returns exit codes and streams", Exec);
            Add(cases, "exec-timeout-truncation", "k8s_exec stops at its timeout and bounds large output", ExecTimeoutTruncation);
            Add(cases, "exec-cancellation", "k8s_exec honors cancellation", ExecCancellation);
            Add(cases, "cordon", "k8s_cordon needs allowClusterScoped, then cordons and uncordons", Cordon);
            Add(cases, "readonly-binding", "A ReadOnly binding refuses changes before contacting the cluster", ReadOnlyBinding);
            return new TestSuiteDescriptor(SuiteId, "Kubernetes connector against the k3s testbed", cases);
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            string? reason = KubernetesTestbed.SkipReason();
            bool skip = reason != null;
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body, null, skip, skip ? reason! : null!));
        }

        private static IConnectorTool Tool(string name)
        {
            IConnectorTool? tool = _Connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        private static KubernetesTestResource Admin(string name, Dictionary<string, string>? extra = null, string? allowed = null)
        {
            Dictionary<string, string> s = KubernetesTestbed.Settings(allowed);
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kv in extra) s[kv.Key] = kv.Value;
            }
            return new KubernetesTestResource(name, s, KubernetesTestbed.KubeconfigSecrets());
        }

        private static Task<ToolResult> Run(KubernetesTestResource r, string tool, object args, CancellationToken token, ConnectorInvocationContext? ctx = null)
        {
            return Tool(tool).ExecuteAsync(ctx ?? r.Invocation(), new ToolArguments(JsonSerializer.Serialize(args)), token);
        }

        private static EffectClassification Classify(KubernetesTestResource r, string tool, object args)
        {
            return Tool(tool).Classify(r.Invocation(), new ToolArguments(JsonSerializer.Serialize(args)));
        }

        private static async Task TestConnectionKubeconfig(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-conn"))
            {
                ConnectorValidationResult v = await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(v.IsValid, "validation: " + String.Join("; ", v.Errors.Select(e => e.Field + " " + e.Message)));
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "success: " + h.ErrorCode + " " + h.Message);
                SshAssert.Contains(h.Details["serverVersion"], "k3s", "server version");
                SshAssert.True(h.Details["canListPods"] == "true", "can list pods");
            }
        }

        private static async Task TestConnectionToken(CancellationToken token)
        {
            Dictionary<string, string> s = KubernetesTestbed.Settings();
            s["serverUrl"] = KubernetesTestbed.ServerUrl;
            using (KubernetesTestResource r = new KubernetesTestResource("tb-k8s-token", s, KubernetesTestbed.TokenSecrets()))
            {
                ConnectorValidationResult v = await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(v.IsValid, "validation: " + String.Join("; ", v.Errors.Select(e => e.Field + " " + e.Message)));
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "success: " + h.ErrorCode + " " + h.Message);
                ToolResult pods = await Run(r, "k8s_list", new { kind = "Pod" }, token).ConfigureAwait(false);
                SshAssert.Ok(pods, "list pods with token");
                SshAssert.Contains(pods.Text, "crasher", "pods");
            }
            Dictionary<string, Dictionary<string, string>> bad = KubernetesTestbed.TokenSecrets();
            bad["serviceAccountToken"]["token"] = "not-a-valid-token";
            using (KubernetesTestResource r = new KubernetesTestResource("tb-k8s-badtoken", s, bad))
            {
                ToolResult pods = await Run(r, "k8s_list", new { kind = "Pod" }, token).ConfigureAwait(false);
                SshAssert.Error(pods, "auth_failed", "bad token");
                SshAssert.NotContains(pods.Text, "not-a-valid-token", "token not echoed");
            }
            Dictionary<string, Dictionary<string, string>> noCa = KubernetesTestbed.TokenSecrets();
            noCa.Remove("certificateAuthority");
            using (KubernetesTestResource r = new KubernetesTestResource("tb-k8s-noca", s, noCa))
            {
                ToolResult pods = await Run(r, "k8s_list", new { kind = "Pod" }, token).ConfigureAwait(false);
                SshAssert.Error(pods, "connection_failed", "untrusted server certificate");
            }
        }

        private static async Task TokenRbacDenied(CancellationToken token)
        {
            Dictionary<string, string> s = KubernetesTestbed.Settings();
            s["serverUrl"] = KubernetesTestbed.ServerUrl;
            using (KubernetesTestResource r = new KubernetesTestResource("tb-k8s-rbac", s, KubernetesTestbed.TokenSecrets()))
            {
                ToolResult secrets = await Run(r, "k8s_list", new { kind = "Secret" }, token).ConfigureAwait(false);
                SshAssert.Error(secrets, "permission_denied", "reader may not list secrets");
                SshAssert.Contains(secrets.Text, "RBAC", "message names RBAC");
                ToolResult scale = await Run(r, "k8s_scale", new { name = "web", replicas = 2 }, token).ConfigureAwait(false);
                SshAssert.Error(scale, "permission_denied", "reader may not scale");
            }
        }

        private static async Task ListPods(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-list"))
            {
                ToolResult result = await Run(r, "k8s_list", new { kind = "pods" }, token).ConfigureAwait(false);
                SshAssert.Ok(result, "list");
                SshAssert.Contains(result.Text, "| NAME |", "name column");
                SshAssert.Contains(result.Text, "STATUS", "status column");
                SshAssert.Contains(result.Text, "RESTARTS", "restarts column");
                SshAssert.Contains(result.Text, "crasher", "crasher pod");
                SshAssert.Contains(result.Text, "web-", "web pods");
                SshAssert.Contains(result.StructuredJson, "\"name\":\"crasher\"", "structured names");
                ToolResult deployments = await Run(r, "k8s_list", new { kind = "deploy" }, token).ConfigureAwait(false);
                SshAssert.Ok(deployments, "list deployments");
                SshAssert.Contains(deployments.Text, "UP-TO-DATE", "deployment columns");
                SshAssert.Contains(deployments.Text, "| web |", "web deployment");
            }
        }

        private static async Task ListSelectorPaging(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-page"))
            {
                ToolResult selected = await Run(r, "k8s_list", new { kind = "Pod", labelSelector = "app=web" }, token).ConfigureAwait(false);
                SshAssert.Ok(selected, "selector");
                SshAssert.NotContains(selected.Text, "crasher", "selector excludes crasher");
                ToolResult field = await Run(r, "k8s_list", new { kind = "Pod", fieldSelector = "metadata.name=crasher" }, token).ConfigureAwait(false);
                SshAssert.Ok(field, "field selector");
                SshAssert.Contains(field.Text, "1 pods", "one pod");

                ToolResult first = await Run(r, "k8s_list", new { kind = "Pod", limit = 1 }, token).ConfigureAwait(false);
                SshAssert.Ok(first, "page 1");
                SshAssert.Contains(first.Text, "continue", "continue hint");
                string? next = JsonDocument.Parse(first.StructuredJson ?? "{}").RootElement.GetProperty("continue").GetString();
                SshAssert.True(!String.IsNullOrEmpty(next), "continue token");
                ToolResult second = await Run(r, "k8s_list", new { kind = "Pod", limit = 1, @continue = next }, token).ConfigureAwait(false);
                SshAssert.Ok(second, "page 2");
                SshAssert.True(first.StructuredJson != second.StructuredJson, "different pages");
            }
        }

        private static async Task ListCustomResources(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-crd"))
            {
                ToolResult byKind = await Run(r, "k8s_list", new { kind = "Widget" }, token).ConfigureAwait(false);
                SshAssert.Ok(byKind, "list widgets");
                SshAssert.Contains(byKind.Text, "blue-widget", "instance");
                SshAssert.Contains(byKind.Text, "COLOR", "printer column");
                SshAssert.Contains(byKind.Text, "blue", "color value");
                ToolResult byPlural = await Run(r, "k8s_list", new { kind = "widgets.testbed.hugo.dev" }, token).ConfigureAwait(false);
                SshAssert.Ok(byPlural, "plural.group");
                ToolResult byShort = await Run(r, "k8s_get", new { kind = "wd", name = "blue-widget" }, token).ConfigureAwait(false);
                SshAssert.Ok(byShort, "get by short name");
                SshAssert.Contains(byShort.Text, "color: blue", "spec");
                ToolResult crds = await Run(r, "k8s_list", new { kind = "crd" }, token).ConfigureAwait(false);
                SshAssert.Ok(crds, "list CRDs (cluster-scoped read)");
                SshAssert.Contains(crds.Text, "widgets.testbed.hugo.dev", "crd row");
            }
        }

        private static async Task ListNamespacesFiltered(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-ns"))
            {
                ToolResult ns = await Run(r, "k8s_list", new { kind = "Namespace" }, token).ConfigureAwait(false);
                SshAssert.Ok(ns, "list namespaces");
                SshAssert.Contains(ns.Text, "hugo-test", "allowed namespace");
                SshAssert.NotContains(ns.Text, "hugo-other", "other namespace hidden");
                SshAssert.NotContains(ns.Text, "kube-system", "system namespace hidden");
                ToolResult cms = await Run(r, "k8s_list", new { kind = "ConfigMap", allNamespaces = true }, token).ConfigureAwait(false);
                SshAssert.Ok(cms, "all namespaces");
                SshAssert.Contains(cms.Text, "hugo-config", "allowed configmap");
                SshAssert.NotContains(cms.Text, "other-config", "other namespace configmap hidden");
                SshAssert.Contains(cms.Text, "NAMESPACE", "namespace column");
            }
            using (KubernetesTestResource r = Admin("tb-k8s-ns-all", null, ""))
            {
                ToolResult cms = await Run(r, "k8s_list", new { kind = "ConfigMap", allNamespaces = true }, token).ConfigureAwait(false);
                SshAssert.Ok(cms, "cluster-wide");
                SshAssert.Contains(cms.Text, "other-config", "unrestricted resource sees every namespace");
            }
            using (KubernetesTestResource r = Admin("tb-k8s-ns-two", null, "hugo-test\nhugo-other"))
            {
                ToolResult cms = await Run(r, "k8s_list", new { kind = "ConfigMap", allNamespaces = true }, token).ConfigureAwait(false);
                SshAssert.Ok(cms, "two namespaces");
                SshAssert.Contains(cms.Text, "other-config", "second namespace");
                SshAssert.Contains(cms.Text, "hugo-config", "first namespace");
                SshAssert.NotContains(cms.Text, "kube-system", "no system namespace");
            }
        }

        private static async Task ListClusterScopedReads(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-noclusterreads", new Dictionary<string, string> { { "clusterScopedReads", "false" } }))
            {
                EffectClassification c = Classify(r, "k8s_list", new { kind = "Node" });
                SshAssert.True(c.Rejected, "classification rejects nodes");
                ToolResult nodes = await Run(r, "k8s_list", new { kind = "Node" }, token).ConfigureAwait(false);
                SshAssert.Error(nodes, "rejected", "nodes refused");
                ToolResult crd = await Run(r, "k8s_list", new { kind = "widgets.testbed.hugo.dev" }, token).ConfigureAwait(false);
                SshAssert.Ok(crd, "namespaced custom resources still readable");
            }
            using (KubernetesTestResource r = Admin("tb-k8s-clusterreads"))
            {
                ToolResult nodes = await Run(r, "k8s_list", new { kind = "Node" }, token).ConfigureAwait(false);
                SshAssert.Ok(nodes, "nodes readable by default");
                SshAssert.Contains(nodes.Text, "Ready", "node status");
            }
        }

        private static async Task NamespaceEnforcement(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-enforce"))
            {
                string other = KubernetesTestbed.OtherNamespace;
                List<KeyValuePair<string, object>> calls = new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("k8s_list", new { kind = "ConfigMap", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_get", new { kind = "ConfigMap", name = "other-config", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_get", new { kind = "Namespace", name = other }),
                    new KeyValuePair<string, object>("k8s_describe", new { kind = "ConfigMap", name = "other-config", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_events", new { @namespace = other }),
                    new KeyValuePair<string, object>("k8s_logs", new { pod = "x", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_top", new { @namespace = other }),
                    new KeyValuePair<string, object>("k8s_rollout_status", new { name = "x", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_rollout_history", new { name = "x", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_rollout_restart", new { name = "x", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_scale", new { name = "x", @namespace = other, replicas = 1 }),
                    new KeyValuePair<string, object>("k8s_apply", new { manifest = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: sneaky\n  namespace: " + other + "\n" }),
                    new KeyValuePair<string, object>("k8s_delete", new { kind = "ConfigMap", name = "other-config", @namespace = other }),
                    new KeyValuePair<string, object>("k8s_exec", new { pod = "x", @namespace = other, command = new[] { "true" } })
                };
                foreach (KeyValuePair<string, object> call in calls)
                {
                    EffectClassification c = Classify(r, call.Key, call.Value);
                    SshAssert.True(c.Rejected, call.Key + " classification rejected: " + c.Reason);
                    ToolResult result = await Run(r, call.Key, call.Value, token).ConfigureAwait(false);
                    SshAssert.Error(result, "rejected", call.Key);
                    SshAssert.Contains(result.Text, "allowed namespaces", call.Key + " message");
                }
                using (KubernetesTestResource open = Admin("tb-k8s-enforce-check", null, ""))
                {
                    ToolResult stillThere = await Run(open, "k8s_get", new { kind = "ConfigMap", name = "other-config", @namespace = other }, token).ConfigureAwait(false);
                    SshAssert.Ok(stillThere, "other-config untouched");
                }
            }
        }

        private static async Task GetYaml(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-get"))
            {
                ToolResult yaml = await Run(r, "k8s_get", new { kind = "Deployment", name = "web" }, token).ConfigureAwait(false);
                SshAssert.Ok(yaml, "get");
                SshAssert.Contains(yaml.Text, "kind: Deployment", "kind");
                SshAssert.Contains(yaml.Text, "replicas: 2", "replicas");
                SshAssert.Contains(yaml.Text, "image: busybox:1.37.0", "image");
                SshAssert.NotContains(yaml.Text, "managedFields", "managedFields stripped");
                ToolResult json = await Run(r, "k8s_get", new { kind = "ConfigMap", name = "hugo-config", output = "json" }, token).ConfigureAwait(false);
                SshAssert.Ok(json, "json");
                using (JsonDocument doc = JsonDocument.Parse(json.Text))
                {
                    SshAssert.True(doc.RootElement.GetProperty("data").GetProperty("greeting").GetString() == "hello from the hugo testbed", "configmap data");
                }
                ToolResult cluster = await Run(r, "k8s_get", new { kind = "Namespace", name = "hugo-test" }, token).ConfigureAwait(false);
                SshAssert.Ok(cluster, "get allowed namespace object");
            }
        }

        private static async Task GetSecretRedacted(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-secret"))
            {
                List<ToolResult> results = new List<ToolResult>
                {
                    await Run(r, "k8s_get", new { kind = "Secret", name = "hugo-secret" }, token).ConfigureAwait(false),
                    await Run(r, "k8s_get", new { kind = "Secret", name = "hugo-secret", output = "json" }, token).ConfigureAwait(false),
                    await Run(r, "k8s_describe", new { kind = "Secret", name = "hugo-secret" }, token).ConfigureAwait(false),
                    await Run(r, "k8s_list", new { kind = "Secret" }, token).ConfigureAwait(false),
                    await Run(r, "k8s_get", new { kind = "Secret", name = "hugo-reader-token" }, token).ConfigureAwait(false)
                };
                string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(KubernetesTestbed.SecretValue));
                string reader = KubernetesTestbed.Read("reader-token").Trim();
                foreach (ToolResult result in results)
                {
                    SshAssert.Ok(result, "secret read");
                    SshAssert.NotContains(result.Text, KubernetesTestbed.SecretValue, "plain value");
                    SshAssert.NotContains(result.Text, KubernetesTestbed.SecretApiKey, "plain api key");
                    SshAssert.NotContains(result.Text, encoded, "base64 value");
                    SshAssert.NotContains(result.Text, reader.Substring(0, 40), "service account token");
                }
                SshAssert.Contains(results[0].Text, "password: \"<redacted: 29 bytes>\"", "redaction marker with size");
                SshAssert.Contains(results[2].Text, "password: 29 bytes", "describe sizes");
            }
        }

        private static async Task GetNotFound(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-404"))
            {
                ToolResult missing = await Run(r, "k8s_get", new { kind = "ConfigMap", name = "does-not-exist" }, token).ConfigureAwait(false);
                SshAssert.Error(missing, "not_found", "missing object");
                ToolResult badKind = await Run(r, "k8s_get", new { kind = "NoSuchKind", name = "x" }, token).ConfigureAwait(false);
                SshAssert.Error(badKind, "not_found", "unknown kind");
            }
        }

        private static async Task Describe(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-describe"))
            {
                ToolResult crasher = await Run(r, "k8s_describe", new { kind = "Pod", name = "crasher" }, token).ConfigureAwait(false);
                SshAssert.Ok(crasher, "describe pod");
                SshAssert.Contains(crasher.Text, "Restart Count:", "restart count");
                SshAssert.Contains(crasher.Text, "Image:          busybox:1.37.0", "image");
                SshAssert.Contains(crasher.Text, "Events:", "events section");
                ToolResult deployment = await Run(r, "k8s_describe", new { kind = "deployment", name = "web" }, token).ConfigureAwait(false);
                SshAssert.Ok(deployment, "describe deployment");
                SshAssert.Contains(deployment.Text, "Replicas:", "replicas");
                SshAssert.Contains(deployment.Text, "Available=True", "conditions");
                ToolResult service = await Run(r, "k8s_describe", new { kind = "svc", name = "web" }, token).ConfigureAwait(false);
                SshAssert.Ok(service, "describe service");
                SshAssert.Contains(service.Text, "80/TCP -> 8080", "ports");
                ToolResult widget = await Run(r, "k8s_describe", new { kind = "Widget", name = "blue-widget" }, token).ConfigureAwait(false);
                SshAssert.Ok(widget, "describe custom resource");
                SshAssert.Contains(widget.Text, "color: blue", "generic spec");
                ToolResult nodes = await Run(r, "k8s_list", new { kind = "Node" }, token).ConfigureAwait(false);
                string node = JsonDocument.Parse(nodes.StructuredJson!).RootElement.GetProperty("items")[0].GetProperty("name").GetString()!;
                ToolResult describeNode = await Run(r, "k8s_describe", new { kind = "Node", name = node }, token).ConfigureAwait(false);
                SshAssert.Ok(describeNode, "describe node");
                SshAssert.Contains(describeNode.Text, "Allocatable:", "allocatable");
            }
        }

        private static async Task<int> WaitForCrasherRestart(KubernetesTestResource r, CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.Add(_CrasherTimeout);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                ToolResult pod = await Run(r, "k8s_get", new { kind = "Pod", name = "crasher", output = "json" }, token).ConfigureAwait(false);
                SshAssert.Ok(pod, "get crasher");
                using (JsonDocument doc = JsonDocument.Parse(pod.Text))
                {
                    int restarts = 0;
                    if (doc.RootElement.TryGetProperty("status", out JsonElement status) && status.TryGetProperty("containerStatuses", out JsonElement cs))
                        restarts = cs[0].GetProperty("restartCount").GetInt32();
                    if (restarts >= 1) return restarts;
                }
                if (DateTime.UtcNow > deadline) throw new TimeoutException("crasher did not restart within " + _CrasherTimeout.TotalSeconds + " seconds.");
                await Task.Delay(_PollInterval, token).ConfigureAwait(false);
            }
        }

        private static async Task<ToolResult> PollUntil(KubernetesTestResource r, string tool, object args, Func<ToolResult, bool> condition, string expectation, CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.Add(_CrasherTimeout);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                ToolResult result = await Run(r, tool, args, token).ConfigureAwait(false);
                if (!result.IsError && condition(result)) return result;
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException(tool + " did not show " + expectation + " within " + _CrasherTimeout.TotalSeconds + " seconds (polled every " + _PollInterval.TotalSeconds + " seconds). Last result: " + result.Text);
                await Task.Delay(_PollInterval, token).ConfigureAwait(false);
            }
        }

        private static async Task Events(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-events"))
            {
                await WaitForCrasherRestart(r, token).ConfigureAwait(false);
                ToolResult all = await Run(r, "k8s_events", new { sinceMinutes = 120 }, token).ConfigureAwait(false);
                SshAssert.Ok(all, "events");
                SshAssert.Contains(all.Text, "LAST SEEN", "columns");
                ToolResult warnings = await PollUntil(
                    r,
                    "k8s_events",
                    new { type = "Warning", kind = "Pod", name = "crasher", sinceMinutes = 120 },
                    res => res.Text.Contains("BackOff", StringComparison.Ordinal),
                    "a Warning BackOff event for pod crasher",
                    token).ConfigureAwait(false);
                SshAssert.Ok(warnings, "warnings");
                SshAssert.Contains(warnings.Text, "BackOff", "crash loop back-off");
                SshAssert.NotContains(warnings.Text, "web-", "filtered to crasher");
                ToolResult everywhere = await Run(r, "k8s_events", new { allNamespaces = true, limit = 5 }, token).ConfigureAwait(false);
                SshAssert.Ok(everywhere, "all allowed namespaces");
            }
        }

        private static async Task Logs(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-logs"))
            {
                ToolResult pods = await Run(r, "k8s_list", new { kind = "Pod", labelSelector = "app=web" }, token).ConfigureAwait(false);
                string web = JsonDocument.Parse(pods.StructuredJson!).RootElement.GetProperty("items")[0].GetProperty("name").GetString()!;
                ToolResult current = await Run(r, "k8s_logs", new { pod = web }, token).ConfigureAwait(false);
                SshAssert.Ok(current, "logs");
                SshAssert.Contains(current.Text, "web fixture starting", "log line");
                SshAssert.Contains(current.Text, "container: web", "header");
                ToolResult stamped = await Run(r, "k8s_logs", new { pod = web, timestamps = true, tailLines = 1 }, token).ConfigureAwait(false);
                SshAssert.Ok(stamped, "timestamps");
                SshAssert.Contains(stamped.Text, "Z web fixture starting", "timestamp prefix");
                ToolResult noPrevious = await Run(r, "k8s_logs", new { pod = web, previous = true }, token).ConfigureAwait(false);
                SshAssert.True(noPrevious.IsError, "no previous instance: " + noPrevious.Text);

                await WaitForCrasherRestart(r, token).ConfigureAwait(false);
                ToolResult previous = await PollUntil(
                    r,
                    "k8s_logs",
                    new { pod = "crasher", previous = true },
                    res => res.Text.Contains("crasher-marker starting", StringComparison.Ordinal),
                    "the previous crasher instance's stdout",
                    token).ConfigureAwait(false);
                SshAssert.Ok(previous, "previous logs");
                SshAssert.Contains(previous.Text, "crasher-marker starting", "stdout of the crashed instance");
                SshAssert.Contains(previous.Text, "previous instance", "header");
                ToolResult badContainer = await Run(r, "k8s_logs", new { pod = "crasher", container = "nope" }, token).ConfigureAwait(false);
                SshAssert.Error(badContainer, "invalid_argument", "unknown container");
                ToolResult missing = await Run(r, "k8s_logs", new { pod = "no-such-pod" }, token).ConfigureAwait(false);
                SshAssert.Error(missing, "not_found", "missing pod");
            }
        }

        private static async Task TopWithoutMetrics(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-top"))
            {
                ToolResult pods = await Run(r, "k8s_top", new { target = "pods" }, token).ConfigureAwait(false);
                SshAssert.Error(pods, "not_supported", "pods");
                SshAssert.Contains(pods.Text, "metrics-server", "message names metrics-server");
                ToolResult nodes = await Run(r, "k8s_top", new { target = "nodes" }, token).ConfigureAwait(false);
                SshAssert.Error(nodes, "not_supported", "nodes");
            }
        }

        private static async Task RolloutStatusHistory(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-rollout"))
            {
                ToolResult status = await Run(r, "k8s_rollout_status", new { name = "web", waitSeconds = 60 }, token).ConfigureAwait(false);
                SshAssert.Ok(status, "status");
                SshAssert.Contains(status.Text, "successfully rolled out", "done");
                SshAssert.Contains(status.StructuredJson, "\"done\":true", "structured done");
                ToolResult history = await Run(r, "k8s_rollout_history", new { name = "web" }, token).ConfigureAwait(false);
                SshAssert.Ok(history, "history");
                SshAssert.Contains(history.Text, "REVISION", "columns");
                SshAssert.Contains(history.Text, "busybox:1.37.0", "image");
                ToolResult wrongKind = await Run(r, "k8s_rollout_status", new { kind = "Pod", name = "crasher" }, token).ConfigureAwait(false);
                SshAssert.True(wrongKind.IsError, "pods do not roll out");
            }
        }

        private static string DeploymentManifest(string name, int replicas, bool withPort, string? extraLabel = null)
        {
            string labels = "    app: " + name + "\n" + (extraLabel == null ? String.Empty : "    extra: " + extraLabel + "\n");
            return "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: " + name + "\n  labels:\n" + labels
                + "spec:\n  replicas: " + replicas + "\n  selector:\n    matchLabels:\n      app: " + name + "\n  template:\n    metadata:\n      labels:\n        app: " + name + "\n"
                + "    spec:\n      terminationGracePeriodSeconds: 1\n      containers:\n        - name: main\n          image: busybox:1.37.0\n          command: [\"sh\", \"-c\", \"echo started; exec sleep 3600\"]\n"
                + (withPort ? "          ports:\n            - containerPort: 8080\n" : String.Empty);
        }

        private static async Task ApplyLifecycle(CancellationToken token)
        {
            string name = "apply-" + _Run;
            using (KubernetesTestResource r = Admin("tb-k8s-apply"))
            {
                try
                {
                    string manifest = DeploymentManifest(name, 1, true, "one");
                    EffectClassification dryClass = Classify(r, "k8s_apply", new { manifest = manifest, dryRun = true });
                    SshAssert.True(dryClass.Effect == EffectEnum.Read && !dryClass.Rejected, "dry run is Read: " + dryClass.Reason);
                    ToolResult dry = await Run(r, "k8s_apply", new { manifest = manifest, dryRun = true }, token).ConfigureAwait(false);
                    SshAssert.Ok(dry, "dry run");
                    SshAssert.Contains(dry.Text, "would be created", "dry outcome");
                    ToolResult notCreated = await Run(r, "k8s_get", new { kind = "Deployment", name = name }, token).ConfigureAwait(false);
                    SshAssert.Error(notCreated, "not_found", "dry run created nothing");

                    EffectClassification writeClass = Classify(r, "k8s_apply", new { manifest = manifest });
                    SshAssert.True(writeClass.Effect == EffectEnum.Write, "apply is Write: " + writeClass.Reason);
                    SshAssert.Contains(writeClass.ApprovalSummary, "Deployment hugo-test/" + name, "summary");
                    ToolResult created = await Run(r, "k8s_apply", new { manifest = manifest }, token).ConfigureAwait(false);
                    SshAssert.Ok(created, "create");
                    SshAssert.Contains(created.Text, "created", "created");

                    ToolResult unchanged = await Run(r, "k8s_apply", new { manifest = manifest }, token).ConfigureAwait(false);
                    SshAssert.Ok(unchanged, "re-apply");
                    SshAssert.Contains(unchanged.Text, "unchanged", "unchanged");

                    ToolResult configured = await Run(r, "k8s_apply", new { manifest = DeploymentManifest(name, 1, true, "two") }, token).ConfigureAwait(false);
                    SshAssert.Ok(configured, "configure");
                    SshAssert.Contains(configured.Text, "configured", "configured");

                    string withoutPort = DeploymentManifest(name, 1, false, "two");
                    ToolResult refused = await Run(r, "k8s_apply", new { manifest = withoutPort }, token).ConfigureAwait(false);
                    SshAssert.Error(refused, "invalid_argument", "field removal refused");
                    SshAssert.Contains(refused.Text, "ports", "names the removed field");
                    SshAssert.Contains(refused.Text, "Nothing was applied", "nothing applied");
                    ToolResult stillHasPort = await Run(r, "k8s_get", new { kind = "Deployment", name = name }, token).ConfigureAwait(false);
                    SshAssert.Contains(stillHasPort.Text, "containerPort: 8080", "port kept");

                    EffectClassification removalClass = Classify(r, "k8s_apply", new { manifest = withoutPort, allowFieldRemoval = true });
                    SshAssert.True(removalClass.Effect == EffectEnum.Destructive, "allowFieldRemoval is Destructive");
                    ToolResult removed = await Run(r, "k8s_apply", new { manifest = withoutPort, allowFieldRemoval = true }, token).ConfigureAwait(false);
                    SshAssert.Ok(removed, "removal allowed");
                    SshAssert.Contains(removed.Text, "removed", "outcome names removal");
                    ToolResult noPort = await Run(r, "k8s_get", new { kind = "Deployment", name = name }, token).ConfigureAwait(false);
                    SshAssert.NotContains(noPort.Text, "containerPort: 8080", "port removed");

                    ToolResult invalid = await Run(r, "k8s_apply", new { manifest = "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: " + name + "-bad\nspec:\n  replicas: \"lots\"\n" }, token).ConfigureAwait(false);
                    SshAssert.Error(invalid, "invalid_argument", "server validation");
                    ToolResult garbage = await Run(r, "k8s_apply", new { manifest = "kind: [unclosed" }, token).ConfigureAwait(false);
                    SshAssert.Error(garbage, "invalid_argument", "unparseable manifest");
                }
                finally
                {
                    await Run(r, "k8s_delete", new { kind = "Deployment", name = name }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static async Task ApplyFieldManager(CancellationToken token)
        {
            string name = "manager-" + _Run;
            string manifest = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: " + name + "\ndata:\n  owner: ";
            using (KubernetesTestResource first = Admin("tb-k8s-manager-a", new Dictionary<string, string> { { "fieldManager", "hugo-tb-a" } }))
            using (KubernetesTestResource second = Admin("tb-k8s-manager-b", new Dictionary<string, string> { { "fieldManager", "hugo-tb-b" } }))
            {
                try
                {
                    SshAssert.Ok(await Run(first, "k8s_apply", new { manifest = manifest + "a\n" }, token).ConfigureAwait(false), "first manager creates");
                    ToolResult conflict = await Run(second, "k8s_apply", new { manifest = manifest + "b\n" }, token).ConfigureAwait(false);
                    SshAssert.Error(conflict, "invalid_argument", "a second field manager conflicts on a field the first one owns");
                    SshAssert.Contains(conflict.Text, "hugo-tb-a", "the conflict names the configured manager");
                    SshAssert.Ok(await Run(second, "k8s_apply", new { manifest = manifest + "b\n", force = true }, token).ConfigureAwait(false), "force takes the field over");
                }
                finally
                {
                    await Run(first, "k8s_delete", new { kind = "ConfigMap", name = name }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static async Task ApplyCustomAndRole(CancellationToken token)
        {
            string widget = "green-" + _Run;
            string role = "role-" + _Run;
            using (KubernetesTestResource r = Admin("tb-k8s-apply-crd"))
            {
                try
                {
                    string widgetManifest = "apiVersion: testbed.hugo.dev/v1\nkind: Widget\nmetadata:\n  name: " + widget + "\nspec:\n  color: green\n  size: 7\n";
                    ToolResult created = await Run(r, "k8s_apply", new { manifest = widgetManifest }, token).ConfigureAwait(false);
                    SshAssert.Ok(created, "apply widget");
                    ToolResult listed = await Run(r, "k8s_list", new { kind = "Widget" }, token).ConfigureAwait(false);
                    SshAssert.Contains(listed.Text, widget, "widget listed");
                    SshAssert.Contains(listed.Text, "green", "widget color");

                    string roleManifest = "apiVersion: rbac.authorization.k8s.io/v1\nkind: Role\nmetadata:\n  name: " + role + "\nrules:\n  - apiGroups: [\"\"]\n    resources: [\"pods\"]\n    verbs: [\"get\"]\n";
                    EffectClassification c = Classify(r, "k8s_apply", new { manifest = roleManifest });
                    SshAssert.True(c.Effect == EffectEnum.Destructive && !c.Rejected, "Role is a destructive kind: " + c.Reason);
                    ToolResult applied = await Run(r, "k8s_apply", new { manifest = roleManifest }, token).ConfigureAwait(false);
                    SshAssert.Ok(applied, "apply role");

                    string clusterRole = "apiVersion: rbac.authorization.k8s.io/v1\nkind: ClusterRole\nmetadata:\n  name: hugo-" + _Run + "\nrules: []\n";
                    ToolResult refused = await Run(r, "k8s_apply", new { manifest = clusterRole }, token).ConfigureAwait(false);
                    SshAssert.Error(refused, "rejected", "cluster-scoped kind refused by default");

                    string secretManifest = "apiVersion: v1\nkind: Secret\nmetadata:\n  name: applied-" + _Run + "\nstringData:\n  token: applied-secret-value-" + _Run + "\n";
                    ToolResult secret = await Run(r, "k8s_apply", new { manifest = secretManifest }, token).ConfigureAwait(false);
                    SshAssert.Ok(secret, "apply secret");
                    SshAssert.NotContains(secret.Text + secret.StructuredJson, "applied-secret-value-" + _Run, "apply output never echoes secret values");
                    ToolResult read = await Run(r, "k8s_get", new { kind = "Secret", name = "applied-" + _Run }, token).ConfigureAwait(false);
                    SshAssert.NotContains(read.Text, "applied-secret-value-" + _Run, "get redacts applied secret");
                    SshAssert.NotContains(read.Text, "last-applied-configuration: \"{", "no last-applied leak");
                }
                finally
                {
                    await Run(r, "k8s_delete", new { kind = "Widget", name = widget }, CancellationToken.None).ConfigureAwait(false);
                    await Run(r, "k8s_delete", new { kind = "Role", name = role }, CancellationToken.None).ConfigureAwait(false);
                    await Run(r, "k8s_delete", new { kind = "Secret", name = "applied-" + _Run }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static async Task RolloutRestart(CancellationToken token)
        {
            string name = "restart-" + _Run;
            using (KubernetesTestResource r = Admin("tb-k8s-restart"))
            {
                try
                {
                    SshAssert.Ok(await Run(r, "k8s_apply", new { manifest = DeploymentManifest(name, 1, false) }, token).ConfigureAwait(false), "create");
                    ToolResult ready = await Run(r, "k8s_rollout_status", new { name = name, waitSeconds = 120 }, token).ConfigureAwait(false);
                    SshAssert.Contains(ready.StructuredJson, "\"done\":true", "initial rollout: " + ready.Text);
                    EffectClassification c = Classify(r, "k8s_rollout_restart", new { name = name });
                    SshAssert.True(c.Effect == EffectEnum.Write, "restart is Write");
                    ToolResult restarted = await Run(r, "k8s_rollout_restart", new { name = name }, token).ConfigureAwait(false);
                    SshAssert.Ok(restarted, "restart");
                    SshAssert.Contains(restarted.Text, "restartedAt", "stamp");
                    ToolResult done = await Run(r, "k8s_rollout_status", new { name = name, waitSeconds = 120 }, token).ConfigureAwait(false);
                    SshAssert.Contains(done.StructuredJson, "\"done\":true", "rollout after restart: " + done.Text);
                    ToolResult history = await Run(r, "k8s_rollout_history", new { name = name }, token).ConfigureAwait(false);
                    SshAssert.Contains(history.Text, "2 revision(s)", "two revisions: " + history.Text);
                }
                finally
                {
                    await Run(r, "k8s_delete", new { kind = "Deployment", name = name }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static async Task Scale(CancellationToken token)
        {
            string name = "scale-" + _Run;
            using (KubernetesTestResource r = Admin("tb-k8s-scale"))
            {
                try
                {
                    SshAssert.Ok(await Run(r, "k8s_apply", new { manifest = DeploymentManifest(name, 1, false) }, token).ConfigureAwait(false), "create");
                    SshAssert.True(Classify(r, "k8s_scale", new { name = name, replicas = 2 }).Effect == EffectEnum.Write, "scale up is Write");
                    ToolResult up = await Run(r, "k8s_scale", new { name = name, replicas = 2 }, token).ConfigureAwait(false);
                    SshAssert.Ok(up, "scale up");
                    SshAssert.Contains(up.Text, "from 1 to 2", "counts");
                    ToolResult get = await Run(r, "k8s_get", new { kind = "Deployment", name = name }, token).ConfigureAwait(false);
                    SshAssert.Contains(get.Text, "replicas: 2", "spec updated");
                    SshAssert.True(Classify(r, "k8s_scale", new { name = name, replicas = 0 }).Effect == EffectEnum.Destructive, "scale to zero is Destructive");
                    ToolResult down = await Run(r, "k8s_scale", new { name = name, replicas = 0 }, token).ConfigureAwait(false);
                    SshAssert.Ok(down, "scale to zero");
                    SshAssert.Contains(down.Text, "from 2 to 0", "counts");
                    ToolResult notScalable = await Run(r, "k8s_scale", new { kind = "ConfigMap", name = "hugo-config", replicas = 1 }, token).ConfigureAwait(false);
                    SshAssert.Error(notScalable, "not_supported", "configmaps have no scale");
                }
                finally
                {
                    await Run(r, "k8s_delete", new { kind = "Deployment", name = name }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static async Task Delete(CancellationToken token)
        {
            string name = "delete-" + _Run;
            using (KubernetesTestResource r = Admin("tb-k8s-delete"))
            {
                string manifest = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: " + name + "\ndata:\n  a: b\n";
                SshAssert.Ok(await Run(r, "k8s_apply", new { manifest = manifest }, token).ConfigureAwait(false), "create");
                SshAssert.True(Classify(r, "k8s_delete", new { kind = "ConfigMap", name = name, dryRun = true }).Effect == EffectEnum.Read, "dry run Read");
                SshAssert.True(Classify(r, "k8s_delete", new { kind = "ConfigMap", name = name }).Effect == EffectEnum.Destructive, "delete Destructive");
                ToolResult dry = await Run(r, "k8s_delete", new { kind = "ConfigMap", name = name, dryRun = true }, token).ConfigureAwait(false);
                SshAssert.Ok(dry, "dry run");
                SshAssert.Ok(await Run(r, "k8s_get", new { kind = "ConfigMap", name = name }, token).ConfigureAwait(false), "still there");
                ToolResult deleted = await Run(r, "k8s_delete", new { kind = "ConfigMap", name = name }, token).ConfigureAwait(false);
                SshAssert.Ok(deleted, "delete");
                SshAssert.Error(await Run(r, "k8s_get", new { kind = "ConfigMap", name = name }, token).ConfigureAwait(false), "not_found", "gone");
                SshAssert.Error(await Run(r, "k8s_delete", new { kind = "ConfigMap", name = name }, token).ConfigureAwait(false), "not_found", "delete again");
                ToolResult ns = await Run(r, "k8s_delete", new { kind = "Namespace", name = "hugo-test" }, token).ConfigureAwait(false);
                SshAssert.Error(ns, "rejected", "namespace delete refused without allowClusterScoped");
            }
        }

        private static async Task<string> WebPod(KubernetesTestResource r, CancellationToken token)
        {
            ToolResult pods = await Run(r, "k8s_list", new { kind = "Pod", labelSelector = "app=web", fieldSelector = "status.phase=Running" }, token).ConfigureAwait(false);
            SshAssert.Ok(pods, "web pods");
            return JsonDocument.Parse(pods.StructuredJson!).RootElement.GetProperty("items")[0].GetProperty("name").GetString()!;
        }

        private static async Task Exec(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-exec"))
            {
                string pod = await WebPod(r, token).ConfigureAwait(false);
                EffectClassification c = Classify(r, "k8s_exec", new { pod = pod, command = new[] { "cat", "/www/index.html" } });
                SshAssert.True(c.Effect == EffectEnum.Destructive, "exec is Destructive");
                SshAssert.Contains(c.ApprovalSummary, "cat /www/index.html", "summary names the command");
                ToolResult ok = await Run(r, "k8s_exec", new { pod = pod, command = new[] { "cat", "/www/index.html" } }, token).ConfigureAwait(false);
                SshAssert.Ok(ok, "exec");
                SshAssert.Contains(ok.Text, "exit_code: 0", "exit code");
                SshAssert.Contains(ok.Text, "hugo", "stdout");
                ToolResult fail = await Run(r, "k8s_exec", new { pod = pod, command = new[] { "sh", "-c", "echo out; echo err >&2; exit 3" } }, token).ConfigureAwait(false);
                SshAssert.Contains(fail.Text, "exit_code: 3", "non-zero exit code: " + fail.Text);
                SshAssert.Contains(fail.Text, "--- stderr (4 bytes) ---\nerr", "stderr separated");
                ToolResult missing = await Run(r, "k8s_exec", new { pod = pod, command = new[] { "no-such-binary" } }, token).ConfigureAwait(false);
                SshAssert.True(missing.IsError || !missing.Text.Contains("exit_code: 0", StringComparison.Ordinal), "missing binary fails: " + missing.Text);
                ToolResult notRunning = await Run(r, "k8s_exec", new { pod = "no-such-pod", command = new[] { "true" } }, token).ConfigureAwait(false);
                SshAssert.Error(notRunning, "not_found", "missing pod");
            }
        }

        private static async Task ExecTimeoutTruncation(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-exec-limits"))
            {
                string pod = await WebPod(r, token).ConfigureAwait(false);
                DateTime started = DateTime.UtcNow;
                ToolResult slow = await Run(r, "k8s_exec", new { pod = pod, command = new[] { "sh", "-c", "echo before; sleep 30" }, timeoutSeconds = 3 }, token).ConfigureAwait(false);
                SshAssert.Error(slow, "timeout", "timeout");
                SshAssert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "stopped near the timeout");
                SshAssert.Contains(slow.Text, "before", "partial output kept");
                ToolResult big = await Run(r, "k8s_exec", new { pod = pod, command = new[] { "sh", "-c", "i=0; while [ $i -lt 5000 ]; do echo line-$i-padding-padding-padding; i=$((i+1)); done; echo last-line" } }, token, r.Invocation(maxBytes: 4096)).ConfigureAwait(false);
                SshAssert.Ok(big, "big output");
                SshAssert.True(big.Truncated, "truncated flag");
                SshAssert.Contains(big.Text, "last-line", "end kept");
                SshAssert.NotContains(big.Text, "line-0-padding", "start dropped");
                SshAssert.True(big.Text.Length < 12000, "bounded: " + big.Text.Length);
            }
        }

        private static async Task ExecCancellation(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-exec-cancel"))
            {
                string pod = await WebPod(r, token).ConfigureAwait(false);
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(2));
                    DateTime started = DateTime.UtcNow;
                    bool cancelled = false;
                    try
                    {
                        await Run(r, "k8s_exec", new { pod = pod, command = new[] { "sleep", "30" }, timeoutSeconds = 60 }, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    SshAssert.True(cancelled, "OperationCanceledException");
                    SshAssert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "cancelled promptly");
                }
            }
        }

        private static async Task Cordon(CancellationToken token)
        {
            string node;
            using (KubernetesTestResource r = Admin("tb-k8s-cordon-default"))
            {
                ToolResult nodes = await Run(r, "k8s_list", new { kind = "Node" }, token).ConfigureAwait(false);
                node = JsonDocument.Parse(nodes.StructuredJson!).RootElement.GetProperty("items")[0].GetProperty("name").GetString()!;
                EffectClassification c = Classify(r, "k8s_cordon", new { node = node });
                SshAssert.True(c.Rejected, "rejected without allowClusterScoped");
                ToolResult refused = await Run(r, "k8s_cordon", new { node = node }, token).ConfigureAwait(false);
                SshAssert.Error(refused, "rejected", "cordon refused");
            }
            using (KubernetesTestResource r = Admin("tb-k8s-cordon", new Dictionary<string, string> { { "allowClusterScoped", "true" } }))
            {
                try
                {
                    EffectClassification c = Classify(r, "k8s_cordon", new { node = node });
                    SshAssert.True(c.Effect == EffectEnum.Destructive && !c.Rejected, "Destructive when allowed");
                    ToolResult cordoned = await Run(r, "k8s_cordon", new { node = node, action = "cordon" }, token).ConfigureAwait(false);
                    SshAssert.Ok(cordoned, "cordon");
                    SshAssert.Contains(cordoned.Text, "cordoned", "cordoned");
                    ToolResult described = await Run(r, "k8s_describe", new { kind = "Node", name = node, eventLimit = 0 }, token).ConfigureAwait(false);
                    SshAssert.Contains(described.Text, "Unschedulable: true", "node unschedulable");
                    ToolResult again = await Run(r, "k8s_cordon", new { node = node }, token).ConfigureAwait(false);
                    SshAssert.Contains(again.Text, "already cordoned", "idempotent");
                }
                finally
                {
                    ToolResult uncordoned = await Run(r, "k8s_cordon", new { node = node, action = "uncordon" }, CancellationToken.None).ConfigureAwait(false);
                    SshAssert.Ok(uncordoned, "uncordon");
                }
                ToolResult after = await Run(r, "k8s_describe", new { kind = "Node", name = node, eventLimit = 0 }, token).ConfigureAwait(false);
                SshAssert.Contains(after.Text, "Unschedulable: false", "node schedulable again");
            }
        }

        private static async Task ReadOnlyBinding(CancellationToken token)
        {
            using (KubernetesTestResource r = Admin("tb-k8s-readonly"))
            {
                ConnectorInvocationContext ctx = r.Invocation(AccessModeEnum.ReadOnly);
                ToolResult scale = await Run(r, "k8s_scale", new { name = "web", replicas = 5 }, token, ctx).ConfigureAwait(false);
                SshAssert.Error(scale, "permission_denied", "ReadOnly refuses scale");
                ToolResult apply = await Run(r, "k8s_apply", new { manifest = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: ro-" + _Run + "\n" }, token, ctx).ConfigureAwait(false);
                SshAssert.Error(apply, "permission_denied", "ReadOnly refuses apply");
                ToolResult dry = await Run(r, "k8s_apply", new { manifest = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: ro-" + _Run + "\n", dryRun = true }, token, ctx).ConfigureAwait(false);
                SshAssert.Ok(dry, "ReadOnly allows a dry run");
                ToolResult get = await Run(r, "k8s_get", new { kind = "Deployment", name = "web" }, token, ctx).ConfigureAwait(false);
                SshAssert.Contains(get.Text, "replicas: 2", "web untouched");
            }
        }

        #endregion
    }
}
