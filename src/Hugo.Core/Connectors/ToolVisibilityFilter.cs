namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;

    /// <summary>
    /// Tool visibility (P5.16, plan section 11.3), shared by MCP tools/list, chat tool definitions, the tools route, and
    /// the instruction compiler. A binding offers a tool when its connector has the tool, no rule denies it, the profile
    /// has a model when the tool requires one, and either the tool can read (base Read, or classified per call) or the
    /// binding's effective access mode is not ReadOnly. A tool is listed once per caller with the aliases that offer it
    /// as the resource enum; a tool no binding offers is omitted. Thread-safe.
    /// </summary>
    public static class ToolVisibilityFilter
    {
        #region Public-Methods

        /// <summary>
        /// Whether one binding offers one tool.
        /// </summary>
        /// <param name="bound">Bound resource.</param>
        /// <param name="tool">Tool descriptor.</param>
        /// <param name="profileHasModel">Whether the profile has an inference endpoint.</param>
        /// <returns>True when the tool is usable through the binding.</returns>
        public static bool Offers(BoundResource bound, ToolDescriptor tool, bool profileHasModel)
        {
            if (bound == null || tool == null) return false;
            BindingToolRule? rule = bound.Binding.ToolRules.FirstOrDefault(r => String.Equals(r.ToolName, tool.Name, StringComparison.Ordinal));
            if (rule != null && rule.Rule == ToolRuleEnum.Deny) return false;
            if (tool.RequiresModel && !profileHasModel) return false;
            if (tool.UpdatesKnowledge && !bound.Binding.AllowKnowledgeUpdates) return false;
            if (tool.BaseEffect != EffectEnum.Read && !tool.ClassifiedPerCall && bound.EffectiveAccessMode == AccessModeEnum.ReadOnly) return false;
            return true;
        }

        /// <summary>
        /// Build the caller's tool list.
        /// </summary>
        /// <param name="bound">The profile's usable bindings.</param>
        /// <param name="profileHasModel">Whether the profile has an inference endpoint.</param>
        /// <returns>Tools ordered by name.</returns>
        public static List<CallerTool> Build(IEnumerable<BoundResource> bound, bool profileHasModel)
        {
            Dictionary<string, CallerTool> tools = new Dictionary<string, CallerTool>(StringComparer.Ordinal);
            foreach (BoundResource item in bound ?? new List<BoundResource>())
            {
                foreach (IConnectorTool tool in item.Connector.Tools)
                {
                    ToolDescriptor d = tool.Descriptor;
                    if (!Offers(item, d, profileHasModel)) continue;
                    if (!tools.TryGetValue(d.Name, out CallerTool? caller))
                    {
                        caller = new CallerTool();
                        caller.Name = d.Name;
                        caller.Family = item.Family;
                        caller.Title = String.IsNullOrEmpty(d.Title) ? d.Name : d.Title;
                        caller.Description = d.Description;
                        caller.InputSchemaJson = d.InputSchemaJson;
                        caller.BaseEffect = d.BaseEffect;
                        caller.ClassifiedPerCall = d.ClassifiedPerCall;
                        caller.UpdatesKnowledge = d.UpdatesKnowledge;
                        caller.RequiresModel = d.RequiresModel;
                        caller.KnownEffect = d.BaseEffect == EffectEnum.Read && !d.ClassifiedPerCall ? EffectEnum.Read : null;
                        tools[d.Name] = caller;
                    }
                    BindingToolRule? rule = item.Binding.ToolRules.FirstOrDefault(r => String.Equals(r.ToolName, d.Name, StringComparison.Ordinal));
                    CallerToolResource resource = new CallerToolResource();
                    resource.Alias = item.Binding.Alias;
                    resource.ConnectorType = item.Resource.ConnectorType;
                    resource.AccessMode = item.EffectiveAccessMode;
                    resource.Rule = rule?.Rule.ToString();
                    caller.Resources.Add(resource);
                }
            }
            List<CallerTool> list = tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            foreach (CallerTool tool in list)
            {
                tool.Resources = tool.Resources.OrderBy(r => r.Alias, StringComparer.Ordinal).ToList();
                tool.Description = Describe(tool);
                tool.InputSchemaJson = WithResourceArgument(tool.InputSchemaJson, tool.Resources.Select(r => r.Alias).ToList());
            }
            return list;
        }

        /// <summary>
        /// Add the reserved resource argument to a tool schema: a required string whose enum lists the aliases.
        /// </summary>
        /// <param name="schemaJson">Tool schema JSON.</param>
        /// <param name="aliases">Aliases.</param>
        /// <returns>Schema JSON.</returns>
        public static string WithResourceArgument(string schemaJson, List<string> aliases)
        {
            JsonObject root;
            try
            {
                root = JsonNode.Parse(String.IsNullOrWhiteSpace(schemaJson) ? "{}" : schemaJson) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                root = new JsonObject();
            }
            root["type"] = "object";
            JsonObject properties = root["properties"] as JsonObject ?? new JsonObject();
            JsonArray values = new JsonArray();
            foreach (string alias in aliases) values.Add(alias);
            JsonObject resource = new JsonObject();
            resource["type"] = "string";
            resource["description"] = "Resource to act on (one of the resources this profile has bound for the tool).";
            resource["enum"] = values;
            JsonObject ordered = new JsonObject();
            ordered["resource"] = resource;
            foreach (KeyValuePair<string, JsonNode?> property in properties.ToList())
            {
                if (property.Key == "resource") continue;
                properties.Remove(property.Key);
                ordered[property.Key] = property.Value;
            }
            root["properties"] = ordered;
            JsonArray required = new JsonArray();
            required.Add("resource");
            if (root["required"] is JsonArray existing)
            {
                foreach (JsonNode? item in existing.ToList())
                {
                    if (item is JsonValue value && value.TryGetValue(out string? name) && !String.IsNullOrEmpty(name) && name != "resource") required.Add(name);
                }
            }
            root["required"] = required;
            return root.ToJsonString();
        }

        #endregion

        #region Private-Methods

        private static string Describe(CallerTool tool)
        {
            StringBuilder sb = new StringBuilder(tool.Description.TrimEnd());
            if (sb.Length > 0 && sb[sb.Length - 1] != '.') sb.Append('.');
            sb.Append(sb.Length > 0 ? " " : String.Empty);
            sb.Append("Resources: ");
            sb.Append(String.Join(", ", tool.Resources.Select(r => r.Alias + " (" + r.AccessMode + (r.Rule == ToolRuleEnum.RequireApproval.ToString() ? ", approval required" : String.Empty) + ")")));
            sb.Append('.');
            if (tool.ClassifiedPerCall) sb.Append(" Each call is classified as Read, Write, or Destructive; ReadOnly resources reject Write and Destructive calls.");
            return sb.ToString();
        }

        #endregion
    }
}
