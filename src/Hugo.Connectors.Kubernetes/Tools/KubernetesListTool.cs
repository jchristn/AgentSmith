namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Kubernetes.Models;

    /// <summary>
    /// <c>k8s_list</c>: list objects of any kind (built-in or custom) with the same columns <c>kubectl get</c> prints.
    /// Always Read. Thread-safe.
    /// </summary>
    public class KubernetesListTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""kind"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Kind or resource name: Pod, pods, deploy, Deployment, Node, or a custom resource such as Widget or widgets.example.com."" },
    ""apiVersion"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Optional apiVersion (for example apps/v1) when a kind name exists in several API groups."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace for namespaced kinds. Defaults to the resource's default namespace. Ignored for cluster-scoped kinds."" },
    ""allNamespaces"": { ""type"": ""boolean"", ""description"": ""List across every namespace this resource may access. Default false."" },
    ""labelSelector"": { ""type"": ""string"", ""maxLength"": 1024, ""description"": ""Label selector, for example app=web,tier!=cache."" },
    ""fieldSelector"": { ""type"": ""string"", ""maxLength"": 1024, ""description"": ""Field selector, for example status.phase=Running or spec.nodeName=node-1."" },
    ""limit"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 5000, ""description"": ""Most objects to return. Capped by the resource's maxObjects setting."" },
    ""continue"": { ""type"": ""string"", ""maxLength"": 4096, ""description"": ""Continue token from a previous k8s_list call to fetch the next page (single namespace only)."" }
  },
  ""required"": [""kind""],
  ""additionalProperties"": false
}";

        private const string _TableAccept = "application/json;as=Table;v=v1;g=meta.k8s.io,application/json";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesListTool() : base(new ToolDescriptor
        {
            Name = "k8s_list",
            Title = "List Kubernetes objects",
            Description = "List objects of any kind, including custom resources, with the columns kubectl get prints (status, readiness, restarts, age). "
                + "Filter with labelSelector and fieldSelector, and page with limit and continue. Use allNamespaces to cover every allowed namespace. "
                + "Secrets are listed by name and type only; their values are never returned.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table with one row per object, plus structured JSON with the names and the continue token."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            string kind = Optional(arguments, "kind") ?? "?";
            string? ns = arguments.GetBool("allNamespaces", false) ? null : Optional(arguments, "namespace");
            string summary = "List " + kind + (ns == null ? String.Empty : " in " + ns) + " on " + TargetName(context);
            return Scoped(options, kind, Optional(arguments, "apiVersion"), ns, EffectEnum.Read, "listing objects is read-only", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string kind = Required(arguments, "kind");
            string? apiVersion = Optional(arguments, "apiVersion");
            bool allNamespaces = arguments.GetBool("allNamespaces", false);
            string? labelSelector = Optional(arguments, "labelSelector");
            string? fieldSelector = Optional(arguments, "fieldSelector");
            string? continueToken = Optional(arguments, "continue");
            int limit = Math.Min(arguments.GetInt("limit", options.MaxObjects, 1, 5000), Math.Min(options.MaxObjects, context.Limits.MaxRows));

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            KubernetesResourceType type = await session.Discovery.ResolveAsync(kind, apiVersion, token).ConfigureAwait(false);
            if (!type.Supports("list")) throw new ConnectorException(ConnectorErrorCodes.NotSupported, type.DisplayName + " cannot be listed.");

            List<string?> scopes = new List<string?>();
            bool showNamespace = false;
            if (type.Namespaced)
            {
                if (allNamespaces)
                {
                    List<string>? allowed = KubernetesNamespacePolicy.AllNamespaces(options);
                    if (allowed == null) scopes.Add(null);
                    else scopes.AddRange(allowed);
                    showNamespace = true;
                }
                else
                {
                    scopes.Add(KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace")));
                }
            }
            else
            {
                KubernetesNamespacePolicy.RequireClusterScoped(options, type, false);
                scopes.Add(null);
            }
            if (scopes.Count > 1) continueToken = null;

            List<string> columns = new List<string>();
            List<IList<string?>> rows = new List<IList<string?>>();
            JsonArray items = new JsonArray();
            string? nextToken = null;
            long? remaining = null;
            bool filterNamespaces = String.Equals(type.Kind, "Namespace", StringComparison.Ordinal) && type.Group.Length == 0 && options.AllowedNamespaces.Count > 0;

            foreach (string? scope in scopes)
            {
                if (rows.Count >= limit) break;
                token.ThrowIfCancellationRequested();
                string path = type.Path(scope, null) + KubernetesSession.Query(
                    "labelSelector", labelSelector,
                    "fieldSelector", fieldSelector,
                    "limit", KubernetesSession.Number(limit - rows.Count),
                    "continue", continueToken,
                    "includeObject", "Metadata");
                string body = await session.GetAsync(path, type.DisplayName + (scope == null ? String.Empty : " in namespace " + scope), token, _TableAccept).ConfigureAwait(false);

                KubernetesTable? table = null;
                try
                {
                    table = JsonSerializer.Deserialize<KubernetesTable>(body, KubernetesFormat.CaseInsensitive);
                }
                catch (JsonException)
                {
                    table = null;
                }

                if (table != null && String.Equals(table.Kind, "Table", StringComparison.Ordinal) && table.ColumnDefinitions != null)
                {
                    List<int> visible = new List<int>();
                    for (int i = 0; i < table.ColumnDefinitions.Count; i++)
                        if (table.ColumnDefinitions[i].Priority == 0) visible.Add(i);
                    if (columns.Count == 0)
                    {
                        if (showNamespace || scope == null && type.Namespaced) columns.Add("NAMESPACE");
                        columns.AddRange(visible.Select(i => (table.ColumnDefinitions[i].Name ?? String.Empty).ToUpperInvariant()));
                    }
                    foreach (KubernetesTableRow row in table.Rows ?? new List<KubernetesTableRow>())
                    {
                        if (rows.Count >= limit) break;
                        string? rowNamespace = row.Object?.Metadata?.Namespace ?? scope;
                        string? rowName = row.Object?.Metadata?.Name;
                        if (filterNamespaces && !KubernetesNamespacePolicy.IsAllowed(options, rowName)) continue;
                        List<string?> cells = new List<string?>();
                        if (showNamespace || scope == null && type.Namespaced) cells.Add(rowNamespace ?? String.Empty);
                        foreach (int i in visible) cells.Add(row.Cells != null && i < row.Cells.Count ? KubernetesFormat.Cell(row.Cells[i]) : String.Empty);
                        rows.Add(cells);
                        items.Add(Item(rowNamespace, rowName));
                    }
                    if (scopes.Count == 1)
                    {
                        nextToken = table.Metadata?.Continue;
                        remaining = table.Metadata?.RemainingItemCount;
                    }
                }
                else
                {
                    JsonNode list = KubernetesFormat.ParseNode(body, type.DisplayName);
                    if (columns.Count == 0)
                    {
                        if (type.Namespaced) columns.Add("NAMESPACE");
                        columns.Add("NAME");
                    }
                    if (list["items"] is JsonArray listItems)
                    {
                        foreach (JsonNode? item in listItems)
                        {
                            if (rows.Count >= limit) break;
                            string? itemName = KubernetesManifestParser.StringOf(item?["metadata"]?["name"]);
                            string? itemNamespace = KubernetesManifestParser.StringOf(item?["metadata"]?["namespace"]);
                            if (filterNamespaces && !KubernetesNamespacePolicy.IsAllowed(options, itemName)) continue;
                            List<string?> cells = new List<string?>();
                            if (type.Namespaced) cells.Add(itemNamespace ?? String.Empty);
                            cells.Add(itemName ?? String.Empty);
                            rows.Add(cells);
                            items.Add(Item(itemNamespace, itemName));
                        }
                    }
                    if (scopes.Count == 1) nextToken = KubernetesManifestParser.StringOf(list["metadata"]?["continue"]);
                }
            }

            StringBuilder sb = new StringBuilder();
            string where = !type.Namespaced ? "cluster-scoped" : scopes.Count == 1 && scopes[0] != null ? "namespace " + scopes[0] : (scopes.Count > 1 ? "namespaces " + String.Join(", ", scopes) : "all namespaces");
            sb.Append(rows.Count).Append(' ').Append(type.DisplayName).Append(" (").Append(type.ApiVersion).Append(' ').Append(type.Kind).Append(", ").Append(where).Append(")\n");
            if (rows.Count == 0) sb.Append("No objects found.\n");
            else sb.Append(TextTable.Render(columns, rows));
            if (!String.IsNullOrEmpty(nextToken))
                sb.Append("More objects exist").Append(remaining.HasValue ? " (about " + remaining.Value + " more)" : String.Empty).Append(". Call k8s_list again with continue: \"").Append(nextToken).Append("\".\n");

            JsonObject structured = new JsonObject
            {
                ["kind"] = type.Kind,
                ["apiVersion"] = type.ApiVersion,
                ["resource"] = type.DisplayName,
                ["count"] = rows.Count,
                ["items"] = items,
                ["continue"] = nextToken
            };
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), OutputBudget(context), context.Limits.MaxLines, result);
            result.StructuredJson = structured.ToJsonString();
            result.Metrics["rows"] = rows.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static JsonObject Item(string? ns, string? name)
        {
            JsonObject o = new JsonObject();
            if (!String.IsNullOrEmpty(ns)) o["namespace"] = ns;
            o["name"] = name ?? String.Empty;
            return o;
        }

        #endregion
    }
}
