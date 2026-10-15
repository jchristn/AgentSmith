namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Sample render context for template previews, with optional caller-supplied overrides. Thread-safe.
    /// </summary>
    public static class TemplateSampleContext
    {
        #region Public-Methods

        /// <summary>
        /// Build a sample context covering every documented root.
        /// </summary>
        /// <param name="overrides">Optional JSON object merged over the sample (schemaless user data).</param>
        /// <returns>Context.</returns>
        public static TemplateContext Build(JsonElement? overrides = null)
        {
            TemplateContext context = new TemplateContext();
            context.Set("tenant.id", "ten_sample");
            context.Set("tenant.name", "Sample Tenant");
            context.Set("profile.name", "Hugo");
            context.Set("profile.displayName", "Hugo");
            context.Set("profile.personaName", "Agent Smith");
            context.Set("profile.userAddress", "Mr. Anderson");
            context.Set("profile.description", "Default operations agent.");
            context.Set("user.firstName", "Thomas");
            context.Set("user.lastName", "Anderson");
            context.Set("user.email", "anderson@hugo.local");
            // The values below mirror what the instruction compiler provides, so a preview renders like compilation.
            context.Set("profile.instructionsAppend", String.Empty);
            context.Set("profile.hasBindings", true);
            context.Set("profile.bindingCount", 1);
            context.Set("profile.singleBinding", true);
            context.Set("profile.familySummary", "PostgreSQL (prod-db)");
            context.Set("now.utc", DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            context.Set("compactInstructions", false);
            Dictionary<string, object?> binding = TemplateContext.NewMap();
            binding["alias"] = "prod-db";
            binding["connectorType"] = "postgresql";
            binding["connectorDisplayName"] = "PostgreSQL";
            binding["family"] = "sql";
            binding["accessMode"] = "ReadOnly";
            binding["requireApprovalForDestructive"] = true;
            binding["environment"] = "prod";
            binding["endpoint"] = "db.example.com:5432";
            binding["description"] = "Primary OLTP database.";
            binding["notes"] = "Read replicas only.";
            binding["tools"] = new List<object?>();
            binding["instructions"] = "Use sql_query for read-only queries.";
            context.Set("bindings", new List<object?> { binding });
            Dictionary<string, object?> byFamily = TemplateContext.NewMap();
            byFamily["sql"] = new List<object?> { binding };
            context.Set("bindingsByFamily", byFamily);
            context.Set("limits.maxIterations", 12);
            context.Set("limits.maxToolCallsPerTurn", 24);
            context.Set("limits.toolTimeoutSeconds", 120);
            Dictionary<string, object?> surface = TemplateContext.NewMap();
            surface["name"] = "Chat";
            surface["isChat"] = true;
            surface["isMcp"] = false;
            surface["isA2A"] = false;
            surface["isOpenAI"] = false;
            context.Set("surface", surface);
            context.Set("hugo.version", Constants.ProductVersion);
            if (overrides.HasValue && overrides.Value.ValueKind == JsonValueKind.Object) Merge(context.Root, overrides.Value);
            return context;
        }

        /// <summary>
        /// Convert a JSON element into a context value tree.
        /// </summary>
        /// <param name="element">Element.</param>
        /// <returns>Value.</returns>
        public static object? FromJson(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    Dictionary<string, object?> map = TemplateContext.NewMap();
                    foreach (JsonProperty property in element.EnumerateObject()) map[property.Name] = FromJson(property.Value);
                    return map;
                case JsonValueKind.Array:
                    List<object?> list = new List<object?>();
                    foreach (JsonElement item in element.EnumerateArray()) list.Add(FromJson(item));
                    return list;
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long l)) return l;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }

        #endregion

        #region Private-Methods

        private static void Merge(Dictionary<string, object?> target, JsonElement source)
        {
            foreach (JsonProperty property in source.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object && target.TryGetValue(property.Name, out object? existing) && existing is Dictionary<string, object?> child)
                {
                    Merge(child, property.Value);
                }
                else
                {
                    target[property.Name] = FromJson(property.Value);
                }
            }
        }

        #endregion
    }
}
