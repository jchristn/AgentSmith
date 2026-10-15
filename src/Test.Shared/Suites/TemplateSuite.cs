namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Templates;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Template engine suites: rendering, filters, blocks, includes, secrets, errors, and static analysis.
    /// </summary>
    public static class TemplateSuite
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("variables", "Variables, nested paths, and missing values", Variables));
            cases.Add(Case("filters", "Filters: default (literal and path), upper, lower, trim, json, toml, indent, truncate, join", Filters));
            cases.Add(Case("conditionals", "if, else, and unless blocks", Conditionals));
            cases.Add(Case("each", "each blocks with this, @index, @first, @last, and nested loops", Each));
            cases.Add(Case("standalone", "Block tags alone on a line remove the line", Standalone));
            cases.Add(Case("includes", "Includes render, detect cycles, and enforce depth", Includes));
            cases.Add(Case("secrets", "Secret references are refused unless allowed and are recorded for scrubbing", Secrets));
            cases.Add(Case("errors", "Syntax errors report line and column", Errors));
            cases.Add(Case("analysis", "Static analysis finds variables, includes, unknown roots, misplaced loop variables, and secrets", Analysis));
            cases.Add(Case("raw", "Raw blocks render literally", Raw));
            cases.Add(Case("scope", "Rendering with a starting scope resolves this outside each", Scope));
            return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("templates", "Template engine", cases) };
        }

        private static TestCaseDescriptor Case(string id, string name, Action body)
        {
            return new TestCaseDescriptor("templates", id, name, ct =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static TemplateContext Context()
        {
            TemplateContext context = new TemplateContext();
            context.Set("agent.name", "helper");
            context.Set("agent.description", null);
            context.Set("deployment.model", "sonnet");
            context.Set("deployment.maxTurns", 8);
            context.Set("input.message", "  Hello  ");
            context.Set("input.items", new List<object?> { "a", "b", "c" });
            Dictionary<string, object?> s1 = TemplateContext.NewMap();
            s1["name"] = "one";
            s1["tags"] = new List<object?> { "x", "y" };
            Dictionary<string, object?> s2 = TemplateContext.NewMap();
            s2["name"] = "two";
            s2["tags"] = new List<object?>();
            context.Set("resources.mcpServers", new List<object?> { s1, s2 });
            context.Set("flags.on", true);
            return context;
        }

        private static string R(string template, TemplateContext? context = null, TemplateRenderOptions? options = null)
        {
            return TemplateRenderer.Render(template, context ?? Context(), options);
        }

        private static void Variables()
        {
            Assert.Equal("helper uses sonnet for 8 turns", R("{{agent.name}} uses {{deployment.model}} for {{deployment.maxTurns}} turns"), "variables");
            Assert.Equal("[]", R("[{{agent.description}}{{nothing.here}}]"), "missing values render empty");
            Assert.Equal("3", R("{{input.items.count}}"), "list count");
        }

        private static void Filters()
        {
            Assert.Equal("anonymous", R("{{agent.description | default: \"anonymous\"}}"), "default literal");
            Assert.Equal("helper", R("{{agent.description | default: agent.name}}"), "default path");
            Assert.Equal("HELPER", R("{{agent.name | upper}}"), "upper");
            Assert.Equal("hello", R("{{input.message | trim | lower}}"), "chained");
            Assert.Equal("\"  Hello  \"", R("{{input.message | json}}"), "json string");
            Assert.Equal("[\"a\",\"b\",\"c\"]", R("{{input.items | json}}"), "json list");
            Assert.Equal("[\"a\", \"b\", \"c\"]", R("{{input.items | toml}}"), "toml list");
            Assert.Equal("\"a\\\"b\"", R("{{x | toml}}", Ctx("x", "a\"b")), "toml escaping");
            Assert.Equal("a\n    b", R("{{x | indent: 4}}", Ctx("x", "a\nb")), "indent");
            Assert.Equal("Hel", R("{{x | truncate: 3}}", Ctx("x", "Hello")), "truncate");
            Assert.Equal("a|b|c", R("{{input.items | join: \"|\"}}"), "join");
            Assert.Equal("20", R("{{deployment.missing | default: \"20\"}}"), "default for missing");
        }

        private static void Conditionals()
        {
            Assert.Equal("yes", R("{{#if flags.on}}yes{{else}}no{{/if}}"), "if true");
            Assert.Equal("no", R("{{#if agent.description}}yes{{else}}no{{/if}}"), "if null");
            Assert.Equal("none", R("{{#unless agent.description}}none{{/unless}}"), "unless");
            Assert.Equal("no", R("{{#if x}}yes{{else}}no{{/if}}", Ctx("x", "false")), "string false is falsy");
            Assert.Equal("no", R("{{#if x}}yes{{else}}no{{/if}}", Ctx("x", new List<object?>())), "empty list is falsy");
        }

        private static void Each()
        {
            Assert.Equal("0:a,1:b,2:c", R("{{#each input.items}}{{@index}}:{{this}}{{#unless @last}},{{/unless}}{{/each}}"), "index and last");
            Assert.Equal("one[x y ]two[]", R("{{#each resources.mcpServers}}{{this.name}}[{{#each this.tags}}{{this}} {{/each}}]{{/each}}"), "nested loops");
            Assert.Equal("first", R("{{#each input.items}}{{#if @first}}first{{/if}}{{/each}}"), "first");
        }

        private static void Standalone()
        {
            string template = "start\n{{#if flags.on}}\ninside\n{{/if}}\n{{#each input.items}}\n- {{this}}\n{{/each}}\nend";
            Assert.Equal("start\ninside\n- a\n- b\n- c\nend", R(template), "standalone lines removed");
            Assert.Equal("a yes b", R("a {{#if flags.on}}yes{{/if}} b"), "inline tags keep surrounding text");
        }

        private static void Includes()
        {
            Dictionary<string, string> library = new Dictionary<string, string>
            {
                { "greeting", "Hi {{agent.name}}{{> punctuation}}" },
                { "punctuation", "!" },
                { "loop-a", "{{> loop-b}}" },
                { "loop-b", "{{> loop-a}}" }
            };
            TemplateRenderOptions options = new TemplateRenderOptions();
            options.IncludeResolver = name => library.TryGetValue(name, out string? content) ? content : null;
            Assert.Equal("Hi helper!", R("{{> greeting}}", null, options), "include");
            Assert.True(options.UsedIncludes.Contains("punctuation"), "nested include recorded");
            Throws(() => R("{{> loop-a}}", null, Options(library)), "cycle");
            Throws(() => R("{{> missing}}", null, Options(library)), "not found");
            Throws(() => R("{{> greeting}}"), "includes unavailable");
        }

        private static void Secrets()
        {
            TemplateContext context = Context();
            context.Set("secret.anthropic", "sk-ant-123456");
            Throws(() => R("{{secret.anthropic}}", context), "secrets refused by default");
            TemplateRenderOptions options = new TemplateRenderOptions();
            options.AllowSecrets = true;
            Assert.Equal("Bearer sk-ant-123456", R("Bearer {{secret.anthropic}}", context, options), "secret rendered");
            Assert.True(options.UsedSecretValues.Contains("sk-ant-123456"), "secret recorded for scrubbing");
        }

        private static void Errors()
        {
            try
            {
                TemplateParser.Parse("line one\n{{#if a}}\nunclosed", "t");
                throw new TestAssertionException("Expected a template error.");
            }
            catch (TemplateException e)
            {
                Assert.Equal(2, e.Line, "line");
                Assert.Equal(1, e.Column, "column");
            }
            Throws(() => TemplateParser.Parse("{{/if}}"), "unexpected close");
            Throws(() => TemplateParser.Parse("{{a | nofilter}}"), "unknown filter");
            Throws(() => TemplateParser.Parse("{{a b}}"), "bad path");
            Throws(() => TemplateParser.Parse("{{#each a}}{{/if}}"), "mismatched close");
        }

        private static void Analysis()
        {
            TemplateAnalysis analysis = TemplateAnalyzer.Analyze("{{profile.personaName}} {{#each bindings}}{{this.alias}}{{/each}} {{> builtin/x}}");
            Assert.True(analysis.Valid, "valid template");
            Assert.True(analysis.Variables.Contains("profile.personaName") && analysis.Variables.Contains("bindings"), "variables found");
            Assert.True(analysis.Includes.Contains("builtin/x"), "includes found");
            Assert.True(!TemplateAnalyzer.Analyze("{{bogus.root}}").Valid, "unknown root");
            Assert.True(!TemplateAnalyzer.Analyze("{{this.name}}").Valid, "this outside each");
            Assert.True(!TemplateAnalyzer.Analyze("{{secret.key}}").Valid, "secret in prompt");
            Assert.True(TemplateAnalyzer.Analyze("{{secret.key}}", null, true).Valid, "secret allowed in env");
        }

        private static void Raw()
        {
            Assert.Equal("literal {{agent.name}} and helper", R("{{{{raw}}}}literal {{agent.name}}{{{{/raw}}}} and {{agent.name}}"), "raw");
        }

        private static void Scope()
        {
            RenderScope scope = new RenderScope();
            Dictionary<string, object?> item = TemplateContext.NewMap();
            item["name"] = "github";
            scope.Current = item;
            scope.Count = 1;
            Assert.Equal("--config=mcp_servers.github.url", TemplateRenderer.Render("--config=mcp_servers.{{this.name}}.url", Context(), null, scope), "scoped render");
        }

        private static TemplateContext Ctx(string path, object? value)
        {
            TemplateContext context = new TemplateContext();
            context.Set(path, value);
            return context;
        }

        private static TemplateRenderOptions Options(Dictionary<string, string> library)
        {
            TemplateRenderOptions options = new TemplateRenderOptions();
            options.IncludeResolver = name => library.TryGetValue(name, out string? content) ? content : null;
            return options;
        }

        private static void Throws(Action action, string what)
        {
            try
            {
                action();
            }
            catch (TemplateException)
            {
                return;
            }
            throw new TestAssertionException("Expected a TemplateException: " + what + ".");
        }
    }
}
