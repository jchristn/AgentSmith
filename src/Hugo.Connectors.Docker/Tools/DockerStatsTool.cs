namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>docker_stats</c>: one snapshot of CPU, memory, network, block I/O, and process counts for one container or
    /// every running container, like <c>docker stats --no-stream</c>. Always Read. Thread-safe.
    /// </summary>
    public class DockerStatsTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""container"": { ""type"": ""string"", ""maxLength"": 256, ""description"": ""Container name or ID. Omit to sample every running container (up to the resource's maxStatsContainers)."" },
    ""sortBy"": { ""type"": ""string"", ""enum"": [""cpu"", ""memory"", ""name""], ""description"": ""Sort order. Default cpu (highest first)."" }
  },
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerStatsTool() : base(new ToolDescriptor
        {
            Name = "docker_stats",
            Title = "Show container resource usage",
            Description = "Take one snapshot of CPU percent, memory use and limit, network and block I/O, and process count, for one container or every running one, like docker stats --no-stream. "
                + "Use it for resource pressure and memory leaks. Each sample takes about a second.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table with one row per container."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            string? container = Optional(arguments, "container");
            return Read("reading stats is read-only", "Show resource usage of " + (container == null ? "running containers" : "container " + DockerFormat.Shorten(container, 100)) + " on " + TargetName(context));
        }

        /// <inheritdoc />
        protected override TimeSpan CallTimeout(ConnectorInvocationContext context, DockerOptions options, ToolArguments arguments)
        {
            TimeSpan t = TimeSpan.FromSeconds(options.RequestTimeoutSeconds + 15);
            return context.Timeout < t ? context.Timeout : t;
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            List<string> ids = new List<string>();
            int running;
            if (Optional(arguments, "container") != null)
            {
                ids.Add(Container(arguments));
                running = 1;
            }
            else
            {
                IList<ContainerListResponse> list = await session.Client.Containers.ListContainersAsync(new ContainersListParameters { All = false }, token).ConfigureAwait(false);
                running = list.Count;
                ids.AddRange(list.OrderBy(c => DockerFormat.Name(c.Names), StringComparer.Ordinal).Take(options.MaxStatsContainers).Select(c => c.ID));
            }

            List<Task<ContainerStatsResponse>> samples = ids.Select(id => SampleAsync(session, id, token)).ToList();
            ContainerStatsResponse[] results = await Task.WhenAll(samples).ConfigureAwait(false);
            string sortBy = (Optional(arguments, "sortBy") ?? "cpu").ToLowerInvariant();
            IEnumerable<ContainerStatsResponse> ordered = sortBy == "memory" ? results.OrderByDescending(DockerStatsMath.MemoryUsed)
                : sortBy == "name" ? results.OrderBy(r => r.Name ?? String.Empty, StringComparer.Ordinal)
                : results.OrderByDescending(DockerStatsMath.CpuPercent);

            List<IList<string?>> rows = new List<IList<string?>>();
            foreach (ContainerStatsResponse s in ordered)
            {
                double limit = DockerStatsMath.MemoryLimit(s);
                rows.Add(new List<string?>
                {
                    DockerFormat.ShortId(s.ID),
                    (s.Name ?? String.Empty).TrimStart('/'),
                    DockerStatsMath.CpuPercent(s).ToString("0.00", CultureInfo.InvariantCulture) + "%",
                    DockerFormat.Bytes(DockerStatsMath.MemoryUsed(s)) + " / " + (limit > 0 ? DockerFormat.Bytes(limit) : "-"),
                    DockerStatsMath.MemoryPercent(s).ToString("0.00", CultureInfo.InvariantCulture) + "%",
                    DockerFormat.Bytes(DockerStatsMath.NetworkReceived(s)) + " / " + DockerFormat.Bytes(DockerStatsMath.NetworkSent(s)),
                    DockerFormat.Bytes(DockerStatsMath.BlockBytes(s, "read")) + " / " + DockerFormat.Bytes(DockerStatsMath.BlockBytes(s, "write")),
                    (s.PidsStats?.Current ?? s.NumProcs).ToString(CultureInfo.InvariantCulture)
                });
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(rows.Count).Append(" container(s) sampled");
            if (running > rows.Count) sb.Append(" of ").Append(running).Append(" running (maxStatsContainers is ").Append(options.MaxStatsContainers).Append(')');
            sb.Append('\n');
            if (rows.Count > 0) sb.Append(TextTable.Render(new List<string> { "CONTAINER ID", "NAME", "CPU %", "MEM USAGE / LIMIT", "MEM %", "NET I/O", "BLOCK I/O", "PIDS" }, rows));
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.Metrics["rows"] = rows.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static async Task<ContainerStatsResponse> SampleAsync(DockerSession session, string id, CancellationToken token)
        {
            DockerStatsCollector collector = new DockerStatsCollector();
            await session.Client.Containers.GetContainerStatsAsync(id, new ContainerStatsParameters { Stream = false }, collector, token).ConfigureAwait(false);
            ContainerStatsResponse? sample = collector.Sample;
            if (sample == null) throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The daemon returned no stats for container " + DockerFormat.ShortId(id) + ".");
            return sample;
        }

        #endregion
    }
}
