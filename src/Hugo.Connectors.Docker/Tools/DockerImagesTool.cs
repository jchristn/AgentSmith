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
    /// <c>docker_images</c>: list images, like <c>docker images</c>. Always Read. Thread-safe.
    /// </summary>
    public class DockerImagesTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""reference"": { ""type"": ""string"", ""maxLength"": 512, ""description"": ""Only images matching this reference, for example nginx or nginx:1.27 or myrepo/*."" },
    ""dangling"": { ""type"": ""boolean"", ""description"": ""true lists only untagged (dangling) images; false hides them."" },
    ""all"": { ""type"": ""boolean"", ""description"": ""Include intermediate images. Default false."" }
  },
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerImagesTool() : base(new ToolDescriptor
        {
            Name = "docker_images",
            Title = "List images",
            Description = "List images on the Docker host with tags, IDs, sizes, ages, and how many containers use each, like docker images. "
                + "Filter by reference or dangling. Use it for disk usage questions and to confirm which image version is present.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table of images, largest first."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            return Read("listing images is read-only", "List images on " + TargetName(context));
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            Dictionary<string, IDictionary<string, bool>> filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal);
            string? reference = Optional(arguments, "reference");
            if (reference != null) filters["reference"] = new Dictionary<string, bool> { { reference, true } };
            if (arguments.Has("dangling")) filters["dangling"] = new Dictionary<string, bool> { { arguments.GetBool("dangling", false) ? "true" : "false", true } };

            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            IList<ImagesListResponse> images = await session.Client.Images.ListImagesAsync(new ImagesListParameters { All = arguments.GetBool("all", false), Filters = filters }, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<IList<string?>> rows = new List<IList<string?>>();
            foreach (ImagesListResponse image in images.OrderByDescending(i => i.Size).Take(context.Limits.MaxRows))
            {
                string tags = image.RepoTags == null || image.RepoTags.Count == 0 ? "<none>:<none>" : String.Join(", ", image.RepoTags);
                rows.Add(new List<string?>
                {
                    tags,
                    DockerFormat.ShortId(image.ID),
                    DockerFormat.Age(image.Created, now),
                    DockerFormat.Bytes(image.Size),
                    image.Containers < 0 ? "-" : image.Containers.ToString(CultureInfo.InvariantCulture)
                });
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(images.Count).Append(" image(s), ").Append(DockerFormat.Bytes(images.Sum(i => (double)i.Size))).Append(" total\n");
            if (rows.Count > 0) sb.Append(TextTable.Render(new List<string> { "REPOSITORY:TAG", "IMAGE ID", "CREATED", "SIZE", "CONTAINERS" }, rows));
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.Metrics["rows"] = rows.Count;
            return result;
        }

        #endregion
    }
}
