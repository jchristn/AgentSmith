namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>k8s_apply</c>: server-side apply of a supplied manifest (one or more objects of any kind). Write; Destructive
    /// for destructive kinds, explicit nulls, <c>force</c>, or <c>allowFieldRemoval</c>; Read for <c>dryRun</c>. Every
    /// object is first dry-run applied, and the call is refused before anything changes when an object would lose
    /// fields and <c>allowFieldRemoval</c> is false. Thread-safe.
    /// </summary>
    public class KubernetesApplyTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""manifest"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 1048576, ""description"": ""YAML (one or more documents separated by ---) or JSON. Every object needs apiVersion, kind, and metadata.name. Send the complete desired object: fields this manager set before and that are missing now are removed."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace for namespaced objects that do not set metadata.namespace. Defaults to the resource's default namespace."" },
    ""dryRun"": { ""type"": ""boolean"", ""description"": ""Validate on the server and report what would happen without changing anything. Read. Default false."" },
    ""force"": { ""type"": ""boolean"", ""description"": ""Take over fields that another field manager owns (resolves apply conflicts). Destructive. Default false."" },
    ""allowFieldRemoval"": { ""type"": ""boolean"", ""description"": ""Allow the apply to remove fields that exist on the live object. Destructive. Default false, which refuses such applies."" }
  },
  ""required"": [""manifest""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesApplyTool() : base(new ToolDescriptor
        {
            Name = "k8s_apply",
            Title = "Apply a manifest",
            Description = "Create or update objects with server-side apply (field manager 'hugo'). Works for every kind, including custom resources. "
                + "Run with dryRun: true first to validate and preview. Write; Destructive when the manifest targets a destructive kind (RBAC, CRDs, namespaces, storage, webhooks), "
                + "sets a field to null, uses force, or allows field removal. Applies that would silently remove fields are refused unless allowFieldRemoval is true.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Write,
            ClassifiedPerCall = true,
            OutputDescription = "One line per object: created, configured, unchanged, or the dry-run outcome."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            return KubernetesManifestClassifier.Classify(
                arguments.GetString("manifest"),
                options,
                Optional(arguments, "namespace"),
                arguments.GetBool("dryRun", false),
                arguments.GetBool("force", false),
                arguments.GetBool("allowFieldRemoval", false),
                TargetName(context));
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            List<JsonObject> objects = KubernetesManifestParser.Parse(arguments.GetString("manifest"));
            string? namespaceArgument = Optional(arguments, "namespace");
            bool dryRun = arguments.GetBool("dryRun", false);
            bool force = arguments.GetBool("force", false);
            bool allowFieldRemoval = arguments.GetBool("allowFieldRemoval", false);
            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);

            List<KubernetesApplyItem> items = new List<KubernetesApplyItem>();
            foreach (JsonObject obj in objects)
            {
                token.ThrowIfCancellationRequested();
                items.Add(await PrepareAsync(session, options, obj, namespaceArgument, dryRun, token).ConfigureAwait(false));
            }

            List<string> problems = new List<string>();
            foreach (KubernetesApplyItem item in items)
            {
                token.ThrowIfCancellationRequested();
                KubernetesApiResponse live = await session.SendAsync(HttpMethod.Get, item.Path, null, null, null, token).ConfigureAwait(false);
                if (live.IsSuccess)
                {
                    item.Existed = true;
                    item.Live = KubernetesFormat.ParseNode(live.Body, item.Label);
                    item.ResourceVersionBefore = KubernetesManifestParser.StringOf(item.Live["metadata"]?["resourceVersion"]);
                }
                else if (live.StatusCode != HttpStatusCode.NotFound)
                {
                    throw KubernetesErrorMapper.FromResponse(live, item.Label);
                }

                string preview = await session.SendCheckedAsync(HttpMethod.Patch, item.Path + Query(options, force, true), item.Body, "application/apply-patch+yaml", item.Label, token).ConfigureAwait(false);
                item.DryRunResult = KubernetesFormat.ParseNode(preview, item.Label);
                if (item.Existed && item.Live != null)
                {
                    item.RemovedFields = KubernetesFieldDiff.RemovedPaths(item.Live, item.DryRunResult);
                    if (item.RemovedFields.Count > 0 && !allowFieldRemoval)
                        problems.Add(item.Label + " would lose " + String.Join(", ", item.RemovedFields.Take(10)) + (item.RemovedFields.Count > 10 ? " and more" : String.Empty));
                }
            }
            if (problems.Count > 0)
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Nothing was applied. The apply would remove fields from live objects: " + String.Join("; ", problems)
                    + ". Include those fields in the manifest, or pass allowFieldRemoval: true (Destructive) if removing them is intended.");

            StringBuilder sb = new StringBuilder();
            JsonArray results = new JsonArray();
            foreach (KubernetesApplyItem item in items)
            {
                string outcome;
                if (dryRun)
                {
                    outcome = item.Existed ? (item.RemovedFields.Count > 0 ? "would be configured, removing " + String.Join(", ", item.RemovedFields.Take(10)) : "would be configured") : "would be created";
                }
                else
                {
                    token.ThrowIfCancellationRequested();
                    string applied = await session.SendCheckedAsync(HttpMethod.Patch, item.Path + Query(options, force, false), item.Body, "application/apply-patch+yaml", item.Label, token).ConfigureAwait(false);
                    JsonNode after = KubernetesFormat.ParseNode(applied, item.Label);
                    string? versionAfter = KubernetesManifestParser.StringOf(after["metadata"]?["resourceVersion"]);
                    if (!item.Existed) outcome = "created";
                    else if (String.Equals(versionAfter, item.ResourceVersionBefore, StringComparison.Ordinal) || KubernetesFieldDiff.SameContent(item.Live, after)) outcome = "unchanged";
                    else outcome = item.RemovedFields.Count > 0 ? "configured (removed " + String.Join(", ", item.RemovedFields.Take(10)) + ")" : "configured";
                    if (String.Equals(item.Type.Kind, "CustomResourceDefinition", StringComparison.Ordinal)) session.Discovery.Reset();
                }
                sb.Append(item.Label).Append(' ').Append(outcome).Append(dryRun ? " (server dry run)" : String.Empty).Append('\n');
                results.Add(new JsonObject { ["kind"] = item.Type.Kind, ["apiVersion"] = item.Type.ApiVersion, ["namespace"] = item.Namespace, ["name"] = item.Name, ["outcome"] = outcome });
            }

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), OutputBudget(context), context.Limits.MaxLines, result);
            result.StructuredJson = new JsonObject { ["dryRun"] = dryRun, ["results"] = results }.ToJsonString();
            return result;
        }

        #endregion

        #region Private-Methods

        private static async Task<KubernetesApplyItem> PrepareAsync(KubernetesSession session, KubernetesOptions options, JsonObject obj, string? namespaceArgument, bool dryRun, CancellationToken token)
        {
            string kind = KubernetesManifestParser.StringOf(obj["kind"]) ?? String.Empty;
            string apiVersion = KubernetesManifestParser.StringOf(obj["apiVersion"]) ?? String.Empty;
            JsonObject metadata = obj["metadata"] as JsonObject ?? new JsonObject();
            string name = KubernetesManifestParser.StringOf(metadata["name"]) ?? String.Empty;
            string? docNamespace = KubernetesManifestParser.StringOf(metadata["namespace"]);

            KubernetesResourceType type;
            try
            {
                type = await session.Discovery.ResolveAsync(kind, apiVersion, token).ConfigureAwait(false);
            }
            catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.NotFound)
            {
                session.Discovery.Reset();
                type = await session.Discovery.ResolveAsync(kind, apiVersion, token).ConfigureAwait(false);
            }
            if (!type.Supports("patch")) throw new ConnectorException(ConnectorErrorCodes.NotSupported, type.DisplayName + " does not support apply.");

            KubernetesApplyItem item = new KubernetesApplyItem();
            item.Type = type;
            item.Name = name;
            if (type.Namespaced)
            {
                if (!String.IsNullOrWhiteSpace(docNamespace) && !String.IsNullOrWhiteSpace(namespaceArgument) && !String.Equals(docNamespace, namespaceArgument, StringComparison.Ordinal))
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, kind + " " + name + " sets namespace '" + docNamespace + "' but the call asked for namespace '" + namespaceArgument + "'.");
                item.Namespace = KubernetesNamespacePolicy.Require(options, String.IsNullOrWhiteSpace(docNamespace) ? namespaceArgument : docNamespace);
                metadata["namespace"] = item.Namespace;
            }
            else
            {
                KubernetesNamespacePolicy.RequireClusterScoped(options, type, !dryRun);
                if (String.Equals(type.Kind, "Namespace", StringComparison.Ordinal) && type.Group.Length == 0 && !KubernetesNamespacePolicy.IsAllowed(options, name))
                    throw KubernetesNamespacePolicy.Denied(options, name);
                metadata.Remove("namespace");
                item.Namespace = null;
            }
            obj["metadata"] = metadata;
            item.Body = obj.ToJsonString();
            item.Path = type.Path(item.Namespace, name);
            item.Label = type.Kind.ToLowerInvariant() + (type.Group.Length == 0 ? String.Empty : "." + type.Group) + " " + KubernetesFormat.Ref(item.Namespace, name);
            return item;
        }

        private static string Query(KubernetesOptions options, bool force, bool dryRun)
        {
            return KubernetesSession.Query("fieldManager", options.FieldManager, "force", force ? "true" : "false", "dryRun", dryRun ? "All" : null);
        }

        #endregion
    }
}
