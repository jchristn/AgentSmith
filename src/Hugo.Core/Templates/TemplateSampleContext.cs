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
            context.Set("profile.description", "Default operations agent.");
            context.Set("persona.name", "Agent Smith");
            context.Set("persona.userAddress", "Mr. Anderson");
            context.Set("user.name", "Thomas Anderson");
            context.Set("user.email", "anderson@hugo.local");
            Dictionary<string, object?> resource = TemplateContext.NewMap();
            resource["alias"] = "prod-db";
            resource["connectorType"] = "postgresql";
            resource["displayName"] = "PostgreSQL";
            resource["environment"] = "prod";
            resource["accessMode"] = "ReadOnly";
            resource["notes"] = "Primary OLTP database.";
            context.Set("resources", new List<object?> { resource });
            context.Set("resourceCount", 1);
            context.Set("hugo.version", Constants.ProductVersion);
            context.Set("now", DateTime.UtcNow);
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
