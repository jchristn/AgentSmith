namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>docker_inspect</c>: the full configuration and state of a container or image, with environment variable
    /// values redacted unless the resource allows them. Always Read. Thread-safe.
    /// </summary>
    public class DockerInspectTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""target"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 512, ""description"": ""Container name or ID, or an image reference when type is image."" },
    ""type"": { ""type"": ""string"", ""enum"": [""container"", ""image""], ""description"": ""What to inspect. Default container."" }
  },
  ""required"": [""target""],
  ""additionalProperties"": false
}";

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerInspectTool() : base(new ToolDescriptor
        {
            Name = "docker_inspect",
            Title = "Inspect a container or image",
            Description = "Return the full JSON of a container (state, exit code, OOM kill, health, restart count, mounts, networks, ports, command, labels) or of an image. "
                + "Environment variable values are redacted unless the resource allows them; names stay visible.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Indented JSON."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            return Read("inspecting is read-only", "Inspect " + (Optional(arguments, "type") ?? "container") + " " + DockerFormat.Shorten(Optional(arguments, "target"), 100) + " on " + TargetName(context));
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            string type = (Optional(arguments, "type") ?? "container").ToLowerInvariant();
            string? target = Optional(arguments, "target");
            if (target == null) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'target' is required.");
            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);

            string json;
            if (type == "image")
            {
                ImageInspectResponse image = await session.Client.Images.InspectImageAsync(target, token).ConfigureAwait(false);
                if (!options.ShowEnvironmentValues && image.Config?.Env != null) image.Config.Env = DockerEnvironmentRedactor.Redact(image.Config.Env);
                json = JsonSerializer.Serialize(image, _Json);
            }
            else if (type == "container")
            {
                string container = Container(arguments, "target");
                ContainerInspectResponse inspected = await session.Client.Containers.InspectContainerAsync(container, token).ConfigureAwait(false);
                if (!options.ShowEnvironmentValues && inspected.Config?.Env != null) inspected.Config.Env = DockerEnvironmentRedactor.Redact(inspected.Config.Env);
                json = JsonSerializer.Serialize(inspected, _Json);
            }
            else
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "type must be container or image.");
            }

            ToolResult result = new ToolResult();
            result.MimeType = "application/json";
            result.Text = OutputLimiter.Head(json, context.Limits.MaxBytes, context.Limits.MaxLines, result);
            return result;
        }

        #endregion
    }
}
