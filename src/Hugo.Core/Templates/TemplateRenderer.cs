namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Renders templates against a <see cref="TemplateContext"/>. Missing values render as empty text; use the
    /// default filter or static analysis to require them. Parsed templates are cached by content. Thread-safe.
    /// </summary>
    public static class TemplateRenderer
    {
        #region Private-Members

        private static readonly ConcurrentDictionary<string, List<TemplateNode>> _Cache = new ConcurrentDictionary<string, List<TemplateNode>>(StringComparer.Ordinal);
        private static readonly int _MaxCacheEntries = 2048;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render a template.
        /// </summary>
        /// <param name="template">Template text.</param>
        /// <param name="context">Context.</param>
        /// <param name="options">Options; null uses defaults (no secrets, no includes).</param>
        /// <param name="scope">Optional starting loop scope, so this and @index resolve outside an each block.</param>
        /// <returns>Rendered text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        /// <exception cref="TemplateException">Thrown on syntax or render errors.</exception>
        public static string Render(string? template, TemplateContext context, TemplateRenderOptions? options = null, RenderScope? scope = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            TemplateRenderOptions opts = options ?? new TemplateRenderOptions();
            StringBuilder output = new StringBuilder();
            Stack<string> includeStack = new Stack<string>();
            List<RenderScope> scopes = new List<RenderScope>();
            if (scope != null) scopes.Add(scope);
            RenderNodes(Parse(template ?? String.Empty, opts.TemplateName), context, scopes, opts, output, includeStack, opts.TemplateName);
            return output.ToString();
        }

        /// <summary>
        /// Evaluate a condition template: true when it renders to non-empty text other than "false".
        /// </summary>
        /// <param name="condition">Condition template; null or empty means true.</param>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <returns>True when the condition holds.</returns>
        public static bool Evaluate(string? condition, TemplateContext context, TemplateRenderOptions? options = null)
        {
            if (String.IsNullOrWhiteSpace(condition)) return true;
            return TemplateValues.IsTruthy(Render(condition, context, options));
        }

        /// <summary>
        /// Parse with caching.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <param name="name">Name for errors.</param>
        /// <returns>Nodes.</returns>
        public static List<TemplateNode> Parse(string template, string? name)
        {
            if (_Cache.TryGetValue(template, out List<TemplateNode>? cached)) return cached;
            List<TemplateNode> nodes = TemplateParser.Parse(template, name);
            if (_Cache.Count > _MaxCacheEntries) _Cache.Clear();
            _Cache[template] = nodes;
            return nodes;
        }

        #endregion

        #region Private-Methods

        private static void RenderNodes(List<TemplateNode> nodes, TemplateContext context, List<RenderScope> scopes, TemplateRenderOptions options, StringBuilder output, Stack<string> includes, string? name)
        {
            foreach (TemplateNode node in nodes)
            {
                switch (node)
                {
                    case TextNode text:
                        output.Append(text.Text);
                        break;
                    case VariableNode variable:
                        output.Append(TemplateValues.ToText(Evaluate(variable, context, scopes, options, name)));
                        break;
                    case IfNode condition:
                        bool truthy = TemplateValues.IsTruthy(Lookup(condition.Path, context, scopes, options, name, condition));
                        if (condition.Negate) truthy = !truthy;
                        RenderNodes(truthy ? condition.Then : condition.Else, context, scopes, options, output, includes, name);
                        break;
                    case EachNode each:
                        object? source = Lookup(each.Path, context, scopes, options, name, each);
                        List<object?> items = AsList(source);
                        for (int i = 0; i < items.Count; i++)
                        {
                            RenderScope scope = new RenderScope();
                            scope.Current = items[i];
                            scope.Index = i;
                            scope.Count = items.Count;
                            scopes.Add(scope);
                            RenderNodes(each.Body, context, scopes, options, output, includes, name);
                            scopes.RemoveAt(scopes.Count - 1);
                        }
                        break;
                    case IncludeNode include:
                        RenderInclude(include, context, scopes, options, output, includes, name);
                        break;
                }
                if (output.Length > options.MaxOutputLength) throw new TemplateException("Rendered output exceeds " + options.MaxOutputLength + " characters.", name, node.Line, node.Column);
            }
        }

        private static void RenderInclude(IncludeNode include, TemplateContext context, List<RenderScope> scopes, TemplateRenderOptions options, StringBuilder output, Stack<string> includes, string? name)
        {
            if (options.IncludeResolver == null) throw new TemplateException("Includes are not available here ({{> " + include.Name + "}}).", name, include.Line, include.Column);
            if (includes.Contains(include.Name)) throw new TemplateException("Include cycle detected: " + String.Join(" > ", includes) + " > " + include.Name + ".", name, include.Line, include.Column);
            if (includes.Count >= options.MaxIncludeDepth) throw new TemplateException("Include depth exceeds " + options.MaxIncludeDepth + ".", name, include.Line, include.Column);
            string? content = options.IncludeResolver(include.Name);
            if (content == null) throw new TemplateException("Included template '" + include.Name + "' was not found.", name, include.Line, include.Column);

            options.UsedIncludes.Add(include.Name);
            includes.Push(include.Name);
            RenderNodes(Parse(content, include.Name), context, scopes, options, output, includes, include.Name);
            includes.Pop();
        }

        private static object? Evaluate(VariableNode variable, TemplateContext context, List<RenderScope> scopes, TemplateRenderOptions options, string? name)
        {
            object? value = Lookup(variable.Path, context, scopes, options, name, variable);
            foreach (TemplateFilter filter in variable.Filters)
            {
                value = Apply(filter, value, context, scopes, options, name, variable);
            }
            return value;
        }

        private static object? Apply(TemplateFilter filter, object? value, TemplateContext context, List<RenderScope> scopes, TemplateRenderOptions options, string? name, TemplateNode node)
        {
            switch (filter.Name)
            {
                case "default":
                    if (!TemplateValues.IsEmpty(value)) return value;
                    if (filter.Argument == null) return String.Empty;
                    return filter.ArgumentIsLiteral ? filter.Argument : Lookup(filter.Argument, context, scopes, options, name, node);
                case "upper":
                    return TemplateValues.ToText(value).ToUpperInvariant();
                case "lower":
                    return TemplateValues.ToText(value).ToLowerInvariant();
                case "trim":
                    return TemplateValues.ToText(value).Trim();
                case "json":
                    return TemplateValues.ToJson(value);
                case "toml":
                    return TemplateValues.ToToml(value);
                case "indent":
                    int spaces = IntArgument(filter, 2, name, node);
                    string pad = new string(' ', Math.Clamp(spaces, 0, 64));
                    return TemplateValues.ToText(value).Replace("\n", "\n" + pad);
                case "truncate":
                    int length = IntArgument(filter, 200, name, node);
                    string text = TemplateValues.ToText(value);
                    return text.Length <= length ? text : text.Substring(0, Math.Max(0, length));
                case "join":
                    string separator = filter.Argument ?? ", ";
                    List<string> parts = new List<string>();
                    foreach (object? item in AsList(value)) parts.Add(TemplateValues.ToText(item));
                    return String.Join(separator, parts);
                default:
                    throw new TemplateException("Unknown filter '" + filter.Name + "'.", name, node.Line, node.Column);
            }
        }

        private static int IntArgument(TemplateFilter filter, int fallback, string? name, TemplateNode node)
        {
            if (filter.Argument == null) return fallback;
            if (Int32.TryParse(filter.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
            throw new TemplateException("Filter '" + filter.Name + "' expects an integer argument.", name, node.Line, node.Column);
        }

        private static object? Lookup(string path, TemplateContext context, List<RenderScope> scopes, TemplateRenderOptions options, string? name, TemplateNode node)
        {
            RenderScope? scope = scopes.Count > 0 ? scopes[scopes.Count - 1] : null;
            if (path == "this") return scope?.Current;
            if (path.StartsWith("this.", StringComparison.Ordinal)) return TemplateContext.Resolve(scope?.Current, path.Substring(5));
            if (path == "@index") return scope?.Index ?? 0;
            if (path == "@first") return scope != null && scope.Index == 0;
            if (path == "@last") return scope != null && scope.Index == scope.Count - 1;

            if (path == "secret" || path.StartsWith("secret.", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.AllowSecrets) throw new TemplateException("Secret references ({{" + path + "}}) are only allowed in harness profile environment variables, files, and MCP server headers.", name, node.Line, node.Column);
                object? secret = context.Get(path);
                string text = TemplateValues.ToText(secret);
                if (text.Length > 0 && secret is string) options.UsedSecretValues.Add(text);
                return secret;
            }

            return context.Get(path);
        }

        private static List<object?> AsList(object? value)
        {
            if (value is List<object?> list) return list;
            if (value == null) return new List<object?>();
            if (value is Dictionary<string, object?> map)
            {
                List<object?> entries = new List<object?>();
                foreach (KeyValuePair<string, object?> entry in map)
                {
                    Dictionary<string, object?> pair = TemplateContext.NewMap();
                    pair["key"] = entry.Key;
                    pair["value"] = entry.Value;
                    entries.Add(pair);
                }
                return entries;
            }
            return new List<object?> { value };
        }

        #endregion
    }
}
