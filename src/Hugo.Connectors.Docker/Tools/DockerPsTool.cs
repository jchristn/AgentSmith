namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>docker_ps</c>: list containers, like <c>docker ps</c>. Always Read. Thread-safe.
    /// </summary>
    public class DockerPsTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""all"": { ""type"": ""boolean"", ""description"": ""Include stopped and exited containers. Default false (running only)."" },
    ""name"": { ""type"": ""string"", ""maxLength"": 256, ""description"": ""Only containers whose name contains this text."" },
    ""label"": { ""type"": ""string"", ""maxLength"": 512, ""description"": ""Only containers with this label, as key or key=value."" },
    ""status"": { ""type"": ""string"", ""enum"": [""created"", ""restarting"", ""running"", ""removing"", ""paused"", ""exited"", ""dead""], ""description"": ""Only containers in this state."" },
    ""image"": { ""type"": ""string"", ""maxLength"": 512, ""description"": ""Only containers created from this image (the ancestor filter)."" },
    ""limit"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 5000, ""description"": ""Most containers to return. Capped by the call's row limit."" }
  },
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerPsTool() : base(new ToolDescriptor
        {
            Name = "docker_ps",
            Title = "List containers",
            Description = "List containers on the Docker host with their image, state, status (uptime, exit code, health), published ports, and age, like docker ps. "
                + "Running containers only unless all is true. Filter by name, label, status, or image. Start here.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table of containers, plus structured JSON with IDs and names."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            return Read("listing containers is read-only", "List containers on " + TargetName(context));
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            Dictionary<string, IDictionary<string, bool>> filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal);
            AddFilter(filters, "name", Optional(arguments, "name"));
            AddFilter(filters, "label", Optional(arguments, "label"));
            AddFilter(filters, "status", Optional(arguments, "status"));
            AddFilter(filters, "ancestor", Optional(arguments, "image"));
            int limit = Math.Min(arguments.GetInt("limit", context.Limits.MaxRows, 1, 5000), context.Limits.MaxRows);
            bool all = arguments.GetBool("all", false) || Optional(arguments, "status") != null;

            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            IList<ContainerListResponse> containers = await session.Client.Containers.ListContainersAsync(new ContainersListParameters { All = all, Filters = filters }, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<ContainerListResponse> shown = containers.OrderByDescending(c => c.Created).Take(limit).ToList();

            List<IList<string?>> rows = new List<IList<string?>>();
            JsonArray items = new JsonArray();
            foreach (ContainerListResponse c in shown)
            {
                string name = DockerFormat.Name(c.Names);
                rows.Add(new List<string?> { DockerFormat.ShortId(c.ID), name, c.Image, c.State, c.Status, DockerFormat.Ports(c.Ports), DockerFormat.Age(c.Created, now) });
                items.Add(new JsonObject { ["id"] = DockerFormat.ShortId(c.ID), ["name"] = name, ["image"] = c.Image, ["state"] = c.State, ["status"] = c.Status });
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(containers.Count).Append(all ? " container(s)" : " running container(s)");
            if (shown.Count < containers.Count) sb.Append(", showing the newest ").Append(shown.Count);
            sb.Append('\n');
            if (rows.Count > 0) sb.Append(TextTable.Render(new List<string> { "CONTAINER ID", "NAMES", "IMAGE", "STATE", "STATUS", "PORTS", "CREATED" }, rows));
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.StructuredJson = new JsonObject { ["count"] = containers.Count, ["containers"] = items }.ToJsonString();
            result.Metrics["rows"] = rows.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static void AddFilter(Dictionary<string, IDictionary<string, bool>> filters, string key, string? value)
        {
            if (String.IsNullOrEmpty(value)) return;
            filters[key] = new Dictionary<string, bool>(StringComparer.Ordinal) { { value, true } };
        }

        #endregion
    }
}
