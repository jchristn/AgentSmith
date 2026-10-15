namespace Test.Shared.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Docker;
    using Test.Shared.Connectors.Ssh;
    using Touchstone.Core;

    /// <summary>
    /// Docker connector integration cases against the Docker-in-Docker testbed (<c>docker/testbed/docker/compose.yaml</c>).
    /// Every tool is exercised. Cases are skipped with a reason when the testbed is not enabled or not reachable.
    /// Containers the cases create carry a unique suffix and are removed afterwards.
    /// </summary>
    public static class DockerTestbedTests
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "testbed.docker";

        #endregion

        #region Private-Members

        private static readonly DockerConnector _Connector = new DockerConnector();
        private static readonly string _Run = Guid.NewGuid().ToString("N").Substring(0, 6);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the testbed suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "test-connection", "Test connection reports the daemon version", TestConnection);
            Add(cases, "ps", "docker_ps lists running containers, all containers, and filters", Ps);
            Add(cases, "inspect", "docker_inspect returns container and image JSON with environment values redacted", Inspect);
            Add(cases, "inspect-show-environment", "showEnvironmentValues reveals environment values", InspectShowEnvironment);
            Add(cases, "logs", "docker_logs reads running and exited containers with tail and timestamps", Logs);
            Add(cases, "stats", "docker_stats samples one and all running containers", Stats);
            Add(cases, "images", "docker_images lists the fixture image", Images);
            Add(cases, "events", "docker_events reads a bounded window with filters", Events);
            Add(cases, "container-actions", "docker_container_action stops, starts, restarts, kills, and removes", ContainerActions);
            Add(cases, "exec", "docker_exec returns exit codes and separate streams", Exec);
            Add(cases, "exec-timeout-truncation", "docker_exec stops at its timeout and bounds large output", ExecTimeoutTruncation);
            Add(cases, "exec-cancellation", "docker_exec honors cancellation", ExecCancellation);
            Add(cases, "not-found", "Unknown containers and images map to not_found", NotFound);
            Add(cases, "readonly-binding", "A ReadOnly binding refuses changes before contacting the daemon", ReadOnlyBinding);
            return new TestSuiteDescriptor(SuiteId, "Docker connector against the Docker-in-Docker testbed", cases);
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            string? reason = DockerTestbed.SkipReason();
            bool skip = reason != null;
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body, null, skip, skip ? reason! : null!));
        }

        private static IConnectorTool Tool(string name)
        {
            IConnectorTool? tool = _Connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        private static DockerTestResource Resource(string name, Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> s = DockerTestbed.Settings();
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kv in extra) s[kv.Key] = kv.Value;
            }
            return new DockerTestResource(name, s, null);
        }

        private static Task<ToolResult> Run(DockerTestResource r, string tool, object args, CancellationToken token, ConnectorInvocationContext? ctx = null)
        {
            return Tool(tool).ExecuteAsync(ctx ?? r.Invocation(), new ToolArguments(JsonSerializer.Serialize(args)), token);
        }

        private static EffectClassification Classify(DockerTestResource r, string tool, object args)
        {
            return Tool(tool).Classify(r.Invocation(), new ToolArguments(JsonSerializer.Serialize(args)));
        }

        private static async Task TestConnection(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-conn"))
            {
                ConnectorValidationResult v = await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(v.IsValid, "validation: " + String.Join("; ", v.Errors.Select(e => e.Field + " " + e.Message)));
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "success: " + h.ErrorCode + " " + h.Message);
                SshAssert.True(h.Details["serverVersion"].StartsWith("29.", StringComparison.Ordinal), "server version " + h.Details["serverVersion"]);
                SshAssert.True(h.Details["tls"] == "false", "plain TCP");
            }
        }

        private static async Task Ps(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-ps"))
            {
                ToolResult running = await Run(r, "docker_ps", new { }, token).ConfigureAwait(false);
                SshAssert.Ok(running, "ps");
                SshAssert.Contains(running.Text, DockerTestbed.WebContainer, "web fixture");
                SshAssert.NotContains(running.Text, DockerTestbed.StoppedContainer, "stopped hidden by default");
                SshAssert.Contains(running.Text, "CONTAINER ID", "columns");
                SshAssert.Contains(running.StructuredJson, "\"name\":\"" + DockerTestbed.WebContainer + "\"", "structured");
                ToolResult all = await Run(r, "docker_ps", new { all = true }, token).ConfigureAwait(false);
                SshAssert.Contains(all.Text, DockerTestbed.StoppedContainer, "stopped shown with all");
                SshAssert.Contains(all.Text, "Exited (3)", "exit code in status");
                ToolResult exited = await Run(r, "docker_ps", new { status = "exited" }, token).ConfigureAwait(false);
                SshAssert.Contains(exited.Text, DockerTestbed.StoppedContainer, "status filter");
                SshAssert.NotContains(exited.Text, DockerTestbed.WebContainer, "status filter excludes running");
                ToolResult byName = await Run(r, "docker_ps", new { all = true, name = "fixture-web" }, token).ConfigureAwait(false);
                SshAssert.Contains(byName.Text, "1 container(s)", "name filter: " + byName.Text);
                ToolResult byLabel = await Run(r, "docker_ps", new { all = true, label = "hugo.testbed=fixture" }, token).ConfigureAwait(false);
                SshAssert.Contains(byLabel.Text, DockerTestbed.StoppedContainer, "label filter");
                ToolResult byImage = await Run(r, "docker_ps", new { image = DockerTestbed.Image }, token).ConfigureAwait(false);
                SshAssert.Contains(byImage.Text, DockerTestbed.WebContainer, "ancestor filter");
            }
        }

        private static async Task Inspect(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-inspect"))
            {
                ToolResult container = await Run(r, "docker_inspect", new { target = DockerTestbed.WebContainer }, token).ConfigureAwait(false);
                SshAssert.Ok(container, "inspect container");
                SshAssert.Contains(container.Text, "FIXTURE_TOKEN=<redacted>", "env name kept, value redacted");
                SshAssert.NotContains(container.Text, DockerTestbed.SecretValue, "secret value");
                SshAssert.Contains(container.Text, "\"Running\": true", "state");
                using (JsonDocument doc = JsonDocument.Parse(container.Text))
                {
                    SshAssert.True(doc.RootElement.GetProperty("Name").GetString() == "/" + DockerTestbed.WebContainer, "Docker API field names");
                }
                ToolResult stopped = await Run(r, "docker_inspect", new { target = DockerTestbed.StoppedContainer }, token).ConfigureAwait(false);
                SshAssert.Contains(stopped.Text, "\"ExitCode\": 3", "exit code");
                ToolResult image = await Run(r, "docker_inspect", new { target = DockerTestbed.Image, type = "image" }, token).ConfigureAwait(false);
                SshAssert.Ok(image, "inspect image");
                SshAssert.Contains(image.Text, "PATH=<redacted>", "image env redacted");
                SshAssert.Contains(image.Text, DockerTestbed.Image, "repo tag");
            }
        }

        private static async Task InspectShowEnvironment(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-inspect-env", new Dictionary<string, string> { { "showEnvironmentValues", "true" } }))
            {
                ToolResult container = await Run(r, "docker_inspect", new { target = DockerTestbed.WebContainer }, token).ConfigureAwait(false);
                SshAssert.Ok(container, "inspect");
                SshAssert.Contains(container.Text, "FIXTURE_TOKEN=" + DockerTestbed.SecretValue, "value shown when allowed");
            }
        }

        private static async Task Logs(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-logs"))
            {
                ToolResult web = await Run(r, "docker_logs", new { container = DockerTestbed.WebContainer, tail = 3 }, token).ConfigureAwait(false);
                SshAssert.Ok(web, "logs");
                SshAssert.Contains(web.Text, "hugo-fixture log line", "log lines");
                string[] lines = web.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                SshAssert.True(lines.Length == 4, "header plus three lines: " + web.Text);
                ToolResult stamped = await Run(r, "docker_logs", new { container = DockerTestbed.WebContainer, tail = 1, timestamps = true }, token).ConfigureAwait(false);
                SshAssert.Contains(stamped.Text, "Z hugo-fixture log line", "timestamps");
                ToolResult since = await Run(r, "docker_logs", new { container = DockerTestbed.WebContainer, since = "3s" }, token).ConfigureAwait(false);
                SshAssert.Ok(since, "since");
                SshAssert.True(since.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 6, "since bounds the window: " + since.Text);
                ToolResult stopped = await Run(r, "docker_logs", new { container = DockerTestbed.StoppedContainer }, token).ConfigureAwait(false);
                SshAssert.Ok(stopped, "exited container logs");
                SshAssert.Contains(stopped.Text, "stopped fixture says goodbye", "line");
                SshAssert.Contains(stopped.Text, "exit code: 3", "header exit code");
                ToolResult stderrOnly = await Run(r, "docker_logs", new { container = DockerTestbed.WebContainer, stream = "stderr", tail = 5 }, token).ConfigureAwait(false);
                SshAssert.Contains(stderrOnly.Text, "(no log lines)", "no stderr");
                ToolResult bounded = await Run(r, "docker_logs", new { container = DockerTestbed.WebContainer, tail = 10000 }, token, r.Invocation(maxBytes: 1024, maxLines: 5)).ConfigureAwait(false);
                SshAssert.True(bounded.Truncated || bounded.Text.Split('\n').Length <= 8, "bounded by limits");
            }
        }

        private static async Task Stats(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-stats"))
            {
                ToolResult one = await Run(r, "docker_stats", new { container = DockerTestbed.WebContainer }, token).ConfigureAwait(false);
                SshAssert.Ok(one, "stats one");
                SshAssert.Contains(one.Text, "1 container(s) sampled", "one row");
                SshAssert.Contains(one.Text, DockerTestbed.WebContainer, "name");
                SshAssert.Contains(one.Text, "MEM USAGE / LIMIT", "columns");
                ToolResult all = await Run(r, "docker_stats", new { sortBy = "memory" }, token).ConfigureAwait(false);
                SshAssert.Ok(all, "stats all");
                SshAssert.Contains(all.Text, DockerTestbed.WebContainer, "running container sampled");
                SshAssert.NotContains(all.Text, DockerTestbed.StoppedContainer, "stopped not sampled");
            }
        }

        private static async Task Images(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-images"))
            {
                ToolResult images = await Run(r, "docker_images", new { }, token).ConfigureAwait(false);
                SshAssert.Ok(images, "images");
                SshAssert.Contains(images.Text, DockerTestbed.Image, "fixture image");
                SshAssert.Contains(images.Text, "REPOSITORY:TAG", "columns");
                ToolResult filtered = await Run(r, "docker_images", new { reference = "busybox" }, token).ConfigureAwait(false);
                SshAssert.Contains(filtered.Text, DockerTestbed.Image, "reference filter");
                ToolResult none = await Run(r, "docker_images", new { reference = "no-such-image-anywhere" }, token).ConfigureAwait(false);
                SshAssert.Contains(none.Text, "0 image(s)", "no match");
            }
        }

        private static async Task Events(CancellationToken token)
        {
            string name = "hugo-events-" + _Run;
            using (DockerTestResource r = Resource("tb-docker-events"))
            {
                try
                {
                    await DockerTestbed.CreateVictimAsync(name, token).ConfigureAwait(false);
                    await Task.Delay(1500, token).ConfigureAwait(false);
                    ToolResult window = await Run(r, "docker_events", new { since = "5m", container = name }, token).ConfigureAwait(false);
                    SshAssert.Ok(window, "events");
                    SshAssert.Contains(window.Text, "start", "start event");
                    SshAssert.Contains(window.Text, name, "actor name");
                    ToolResult creates = await Run(r, "docker_events", new { since = "5m", type = "container", @event = "create", container = name }, token).ConfigureAwait(false);
                    SshAssert.Contains(creates.Text, "1 event(s)", "event filter: " + creates.Text);
                    ToolResult capped = await Run(r, "docker_events", new { since = "30d", limit = 1 }, token).ConfigureAwait(false);
                    SshAssert.Ok(capped, "capped");
                    SshAssert.Contains(capped.Text, "window capped", "window capped");
                    ToolResult future = await Run(r, "docker_events", new { since = "2h", until = "2999-01-01T00:00:00Z" }, token).ConfigureAwait(false);
                    SshAssert.Ok(future, "until clamped to now");
                    ToolResult bad = await Run(r, "docker_events", new { since = "yesterday-ish" }, token).ConfigureAwait(false);
                    SshAssert.Error(bad, "invalid_argument", "bad since");
                }
                finally
                {
                    await DockerTestbed.RemoveQuietlyAsync(name).ConfigureAwait(false);
                }
            }
        }

        private static async Task ContainerActions(CancellationToken token)
        {
            string name = "hugo-victim-" + _Run;
            using (DockerTestResource r = Resource("tb-docker-actions"))
            {
                try
                {
                    await DockerTestbed.CreateVictimAsync(name, token).ConfigureAwait(false);
                    SshAssert.True(Classify(r, "docker_container_action", new { container = name, action = "start" }).Effect == EffectEnum.Write, "start W");
                    SshAssert.True(Classify(r, "docker_container_action", new { container = name, action = "restart" }).Effect == EffectEnum.Write, "restart W");
                    EffectClassification stopClass = Classify(r, "docker_container_action", new { container = name, action = "stop" });
                    SshAssert.True(stopClass.Effect == EffectEnum.Destructive, "stop D");
                    SshAssert.Contains(stopClass.ApprovalSummary, "Stop container " + name, "summary");

                    ToolResult stop = await Run(r, "docker_container_action", new { container = name, action = "stop", timeoutSeconds = 5 }, token).ConfigureAwait(false);
                    SshAssert.Ok(stop, "stop");
                    SshAssert.Contains(stop.Text, "after: exited", "stopped");
                    ToolResult stopAgain = await Run(r, "docker_container_action", new { container = name, action = "stop" }, token).ConfigureAwait(false);
                    SshAssert.Contains(stopAgain.Text, "already stopped", "idempotent stop: " + stopAgain.Text);
                    ToolResult kill = await Run(r, "docker_container_action", new { container = name, action = "kill" }, token).ConfigureAwait(false);
                    SshAssert.Error(kill, "invalid_argument", "kill a stopped container");
                    ToolResult start = await Run(r, "docker_container_action", new { container = name, action = "start" }, token).ConfigureAwait(false);
                    SshAssert.Ok(start, "start");
                    SshAssert.Contains(start.Text, "after: running", "running");
                    ToolResult restart = await Run(r, "docker_container_action", new { container = name, action = "restart", timeoutSeconds = 2 }, token).ConfigureAwait(false);
                    SshAssert.Ok(restart, "restart");
                    SshAssert.Contains(restart.Text, "after: running", "running after restart");
                    ToolResult removeRunning = await Run(r, "docker_container_action", new { container = name, action = "remove" }, token).ConfigureAwait(false);
                    SshAssert.Error(removeRunning, "invalid_argument", "remove running without force");
                    ToolResult killed = await Run(r, "docker_container_action", new { container = name, action = "kill", signal = "SIGKILL" }, token).ConfigureAwait(false);
                    SshAssert.Ok(killed, "kill");
                    SshAssert.Contains(killed.Text, "sent SIGKILL", "signal");
                    ToolResult started = await Run(r, "docker_container_action", new { container = name, action = "start" }, token).ConfigureAwait(false);
                    SshAssert.Ok(started, "start again");
                    EffectClassification forceClass = Classify(r, "docker_container_action", new { container = name, action = "remove", force = true });
                    SshAssert.Contains(forceClass.ApprovalSummary, "(force)", "force in summary");
                    ToolResult removed = await Run(r, "docker_container_action", new { container = name, action = "remove", force = true }, token).ConfigureAwait(false);
                    SshAssert.Ok(removed, "remove force");
                    SshAssert.Contains(removed.Text, "removed", "removed");
                    ToolResult gone = await Run(r, "docker_inspect", new { target = name }, token).ConfigureAwait(false);
                    SshAssert.Error(gone, "not_found", "gone");
                    ToolResult bogus = await Run(r, "docker_container_action", new { container = name, action = "explode" }, token).ConfigureAwait(false);
                    SshAssert.True(bogus.IsError, "unknown action fails");
                    SshAssert.True(Classify(r, "docker_container_action", new { container = name, action = "explode" }).Effect == EffectEnum.Destructive, "unknown action fails closed");
                }
                finally
                {
                    await DockerTestbed.RemoveQuietlyAsync(name).ConfigureAwait(false);
                }
            }
        }

        private static async Task Exec(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-exec"))
            {
                EffectClassification c = Classify(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "cat", "/etc/hostname" } });
                SshAssert.True(c.Effect == EffectEnum.Destructive, "exec D");
                SshAssert.Contains(c.ApprovalSummary, "Run \"cat /etc/hostname\" in container " + DockerTestbed.WebContainer, "summary");
                ToolResult ok = await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "sh", "-c", "echo hello from exec" } }, token).ConfigureAwait(false);
                SshAssert.Ok(ok, "exec");
                SshAssert.Contains(ok.Text, "exit_code: 0", "exit code");
                SshAssert.Contains(ok.Text, "hello from exec", "stdout");
                ToolResult fail = await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "sh", "-c", "echo out; echo err >&2; exit 3" } }, token).ConfigureAwait(false);
                SshAssert.Contains(fail.Text, "exit_code: 3", "non-zero: " + fail.Text);
                SshAssert.Contains(fail.Text, "--- stderr (4 bytes) ---\nerr", "stderr separated");
                ToolResult user = await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "id", "-u" }, user = "65534", workingDir = "/tmp" }, token).ConfigureAwait(false);
                SshAssert.Contains(user.Text, "65534", "user honored");
                ToolResult missing = await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "no-such-binary" } }, token).ConfigureAwait(false);
                SshAssert.True(missing.IsError || !missing.Text.Contains("exit_code: 0", StringComparison.Ordinal), "missing binary fails: " + missing.Text);
                ToolResult stopped = await Run(r, "docker_exec", new { container = DockerTestbed.StoppedContainer, command = new[] { "true" } }, token).ConfigureAwait(false);
                SshAssert.Error(stopped, "invalid_argument", "exec on an exited container");
            }
        }

        private static async Task ExecTimeoutTruncation(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-exec-limits"))
            {
                DateTime started = DateTime.UtcNow;
                ToolResult slow = await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "sh", "-c", "echo before; sleep 30" }, timeoutSeconds = 3 }, token).ConfigureAwait(false);
                SshAssert.Error(slow, "timeout", "timeout");
                SshAssert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "stopped near the timeout");
                SshAssert.Contains(slow.Text, "before", "partial output kept");
                ToolResult big = await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "sh", "-c", "i=0; while [ $i -lt 5000 ]; do echo line-$i-padding-padding-padding; i=$((i+1)); done; echo last-line" } }, token, r.Invocation(maxBytes: 4096)).ConfigureAwait(false);
                SshAssert.Ok(big, "big output");
                SshAssert.True(big.Truncated, "truncated flag");
                SshAssert.Contains(big.Text, "last-line", "end kept");
                SshAssert.NotContains(big.Text, "line-0-padding", "start dropped");
                SshAssert.True(big.Text.Length < 12000, "bounded: " + big.Text.Length);
            }
        }

        private static async Task ExecCancellation(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-exec-cancel"))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                DateTime started = DateTime.UtcNow;
                bool cancelled = false;
                try
                {
                    await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "sleep", "30" }, timeoutSeconds = 60 }, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                SshAssert.True(cancelled, "OperationCanceledException");
                SshAssert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "cancelled promptly");
            }
        }

        private static async Task NotFound(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-404"))
            {
                SshAssert.Error(await Run(r, "docker_inspect", new { target = "no-such-container" }, token).ConfigureAwait(false), "not_found", "inspect");
                SshAssert.Error(await Run(r, "docker_inspect", new { target = "no-such-image:1.0", type = "image" }, token).ConfigureAwait(false), "not_found", "image");
                SshAssert.Error(await Run(r, "docker_logs", new { container = "no-such-container" }, token).ConfigureAwait(false), "not_found", "logs");
                SshAssert.Error(await Run(r, "docker_stats", new { container = "no-such-container" }, token).ConfigureAwait(false), "not_found", "stats");
                SshAssert.Error(await Run(r, "docker_container_action", new { container = "no-such-container", action = "restart" }, token).ConfigureAwait(false), "not_found", "action");
                SshAssert.Error(await Run(r, "docker_exec", new { container = "no-such-container", command = new[] { "true" } }, token).ConfigureAwait(false), "not_found", "exec");
            }
        }

        private static async Task ReadOnlyBinding(CancellationToken token)
        {
            using (DockerTestResource r = Resource("tb-docker-readonly"))
            {
                ConnectorInvocationContext ctx = r.Invocation(AccessModeEnum.ReadOnly);
                ToolResult stop = await Run(r, "docker_container_action", new { container = DockerTestbed.WebContainer, action = "stop" }, token, ctx).ConfigureAwait(false);
                SshAssert.Error(stop, "permission_denied", "ReadOnly refuses stop");
                ToolResult exec = await Run(r, "docker_exec", new { container = DockerTestbed.WebContainer, command = new[] { "true" } }, token, ctx).ConfigureAwait(false);
                SshAssert.Error(exec, "permission_denied", "ReadOnly refuses exec");
                ToolResult ps = await Run(r, "docker_ps", new { }, token, ctx).ConfigureAwait(false);
                SshAssert.Contains(ps.Text, DockerTestbed.WebContainer, "web still running");
            }
        }

        #endregion
    }
}
