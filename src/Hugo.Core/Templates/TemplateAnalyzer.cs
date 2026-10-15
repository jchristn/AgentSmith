namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Static analysis: referenced variables, includes, secret references, unknown context roots, and missing required
    /// variables. Thread-safe.
    /// </summary>
    public static class TemplateAnalyzer
    {
        #region Public-Members

        /// <summary>
        /// Context roots available to templates.
        /// </summary>
        public static IReadOnlyCollection<string> KnownRoots
        {
            get
            {
                return _KnownRoots;
            }
        }

        /// <summary>
        /// One-sentence reference for template authors, used in error messages: the valid variable roots, how to write a
        /// variable, the loop variables, and the include syntax.
        /// </summary>
        public static string SyntaxHint
        {
            get
            {
                return "Valid variable roots: " + String.Join(", ", _KnownRoots.Where(r => !r.StartsWith("@", StringComparison.Ordinal) && r != "this"))
                    + ". Write a variable as {{root.field}}, for example {{profile.name}}, {{user.firstName}}, or {{input.name}} (resource action parameters)"
                    + "; inside {{#each}} use {{this}}, {{@index}}, {{@first}}, and {{@last}}"
                    + "; include another template with {{> key}}, for example {{> persona.agent-smith}} (no 'include' keyword).";
            }
        }

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _KnownRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tenant", "profile", "persona", "user", "now", "bindings", "bindingsByFamily", "binding", "connector", "resources", "resource", "limits", "surface", "tool", "hugo", "input", "conversation", "approval", "secret", "this", "@index", "@first", "@last"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Analyze a template.
        /// </summary>
        /// <param name="template">Template text.</param>
        /// <param name="name">Template name.</param>
        /// <param name="allowSecrets">Whether secret references are permitted.</param>
        /// <param name="loopScope">Whether the template renders inside a loop scope (for example a harness argument with RepeatOver), so this and @index are valid at the top level.</param>
        /// <returns>Analysis.</returns>
        public static TemplateAnalysis Analyze(string? template, string? name = null, bool allowSecrets = false, bool loopScope = false)
        {
            TemplateAnalysis analysis = new TemplateAnalysis();
            List<TemplateNode> nodes;
            try
            {
                nodes = TemplateParser.Parse(template ?? String.Empty, name);
            }
            catch (TemplateException e)
            {
                analysis.Errors.Add(e.Message);
                return analysis;
            }

            Walk(nodes, analysis, allowSecrets, name, loopScope ? 1 : 0);
            analysis.Variables = analysis.Variables.Distinct(StringComparer.Ordinal).ToList();
            analysis.Includes = analysis.Includes.Distinct(StringComparer.Ordinal).ToList();
            analysis.SecretReferences = analysis.SecretReferences.Distinct(StringComparer.Ordinal).ToList();
            return analysis;
        }

        /// <summary>
        /// Analyze and throw on the first problem.
        /// </summary>
        /// <param name="template">Template text.</param>
        /// <param name="name">Template name.</param>
        /// <param name="allowSecrets">Whether secret references are permitted.</param>
        /// <param name="loopScope">Whether the template renders inside a loop scope.</param>
        /// <returns>Analysis.</returns>
        /// <exception cref="TemplateException">Thrown when the template is invalid.</exception>
        public static TemplateAnalysis Validate(string? template, string? name = null, bool allowSecrets = false, bool loopScope = false)
        {
            TemplateAnalysis analysis = Analyze(template, name, allowSecrets, loopScope);
            if (!analysis.Valid) throw new TemplateException(analysis.Errors[0], null);
            return analysis;
        }

        #endregion

        #region Private-Methods

        private static void Walk(List<TemplateNode> nodes, TemplateAnalysis analysis, bool allowSecrets, string? name, int loopDepth)
        {
            foreach (TemplateNode node in nodes)
            {
                switch (node)
                {
                    case VariableNode variable:
                        Reference(variable.Path, node, analysis, allowSecrets, name, loopDepth);
                        foreach (TemplateFilter filter in variable.Filters)
                        {
                            if (!filter.ArgumentIsLiteral && filter.Argument != null) Reference(filter.Argument, node, analysis, allowSecrets, name, loopDepth);
                        }
                        break;
                    case IfNode condition:
                        Reference(condition.Path, node, analysis, allowSecrets, name, loopDepth);
                        Walk(condition.Then, analysis, allowSecrets, name, loopDepth);
                        Walk(condition.Else, analysis, allowSecrets, name, loopDepth);
                        break;
                    case EachNode each:
                        Reference(each.Path, node, analysis, allowSecrets, name, loopDepth);
                        Walk(each.Body, analysis, allowSecrets, name, loopDepth + 1);
                        break;
                    case IncludeNode include:
                        analysis.Includes.Add(include.Name);
                        break;
                }
            }
        }

        private static void Reference(string path, TemplateNode node, TemplateAnalysis analysis, bool allowSecrets, string? name, int loopDepth)
        {
            string root = path.Split('.')[0];
            string where = (String.IsNullOrEmpty(name) ? "template" : "template '" + name + "'") + " at line " + node.Line + ", column " + node.Column + ": ";
            if (!_KnownRoots.Contains(root))
            {
                analysis.Errors.Add(where + "unknown variable root '" + root + "' in '" + path + "'. " + SyntaxHint);
                return;
            }
            if ((root == "this" || root.StartsWith("@", StringComparison.Ordinal)) && loopDepth == 0)
            {
                analysis.Errors.Add(where + "'" + path + "' is only valid inside {{#each}}.");
                return;
            }
            if (root.Equals("secret", StringComparison.OrdinalIgnoreCase))
            {
                analysis.SecretReferences.Add(path);
                if (!allowSecrets) analysis.Errors.Add(where + "secret references are only allowed in harness profile environment variables, files, and MCP server headers.");
                return;
            }
            if (root != "this" && !root.StartsWith("@", StringComparison.Ordinal)) analysis.Variables.Add(path);
        }

        #endregion
    }
}
