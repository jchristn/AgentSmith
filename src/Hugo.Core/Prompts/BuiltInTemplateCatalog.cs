namespace Hugo.Core.Prompts
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Services;

    /// <summary>
    /// The Built-in prompt template layer (P5.3, D-015): the Markdown files embedded from Hugo.Core/Seed/Prompts plus
    /// every registered connector's default instructions under its DefaultInstructionsKey
    /// (connector.&lt;type&gt;.instructions). Each entry carries a content hash so the Global layer can tell when a
    /// shipped default changed. Registered templates are mirrored into <see cref="BuiltInPrompts"/>. Thread-safe.
    /// </summary>
    public class BuiltInTemplateCatalog
    {
        #region Public-Members

        /// <summary>
        /// Key of the composition root template.
        /// </summary>
        public static readonly string RootKey = "instructions.root";

        /// <summary>
        /// Key of the default persona.
        /// </summary>
        public static readonly string DefaultPersonaKey = "persona.agent-smith";

        /// <summary>
        /// Number of templates.
        /// </summary>
        public int Count
        {
            get
            {
                return _Templates.Count;
            }
        }

        #endregion

        #region Private-Members

        private static readonly string _ResourcePrefix = "Hugo.Core.Seed.Prompts.";
        private static readonly Dictionary<string, string> _Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "instructions.root", "Composition root: persona, safety, tool usage, surface preamble, one section per resource, and the profile append." },
            { "persona.agent-smith", "Default persona: Agent Smith, addressing the operator as Mr. Anderson (Appendix A)." },
            { "persona.neutral", "A plain, non-themed persona." },
            { "system.safety", "Safety rules: effects, access modes, approvals, truncation." },
            { "system.tool-usage", "How to use the resource argument, limits, and error codes." },
            { "system.mcp-preamble", "Framing used when the caller's own model drives over MCP." },
            { "system.a2a-preamble", "Framing for A2A tasks." },
            { "section.resource", "Wrapper rendered once per binding." },
            { "a2a.skill-description", "Agent card skill text." }
        };
        private readonly ConcurrentDictionary<string, BuiltInTemplate> _Templates = new ConcurrentDictionary<string, BuiltInTemplate>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the templates embedded in Hugo.Core.
        /// </summary>
        public BuiltInTemplateCatalog()
        {
            Assembly assembly = typeof(BuiltInTemplateCatalog).Assembly;
            foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(_ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)))
            {
                string key = name.Substring(_ResourcePrefix.Length, name.Length - _ResourcePrefix.Length - 3);
                using (Stream? stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null) continue;
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        Register(key, reader.ReadToEnd(), "hugo", _Descriptions.TryGetValue(key, out string? d) ? d : String.Empty);
                    }
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// SHA-256 of a text, lowercase hex. Line endings are normalized to LF first.
        /// </summary>
        /// <param name="content">Text.</param>
        /// <returns>Hash.</returns>
        public static string HashOf(string? content)
        {
            string normalized = (content ?? String.Empty).Replace("\r\n", "\n");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }

        /// <summary>
        /// Register or replace a Built-in template.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="content">Content.</param>
        /// <param name="source">Source (hugo or a connector type).</param>
        /// <param name="description">Description.</param>
        /// <returns>The template.</returns>
        /// <exception cref="ArgumentNullException">Thrown when key is null or empty.</exception>
        public BuiltInTemplate Register(string key, string content, string source, string description)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            BuiltInTemplate template = new BuiltInTemplate();
            template.Key = key;
            template.Content = (content ?? String.Empty).Replace("\r\n", "\n").TrimEnd('\n') + "\n";
            template.Hash = HashOf(template.Content);
            template.Source = String.IsNullOrEmpty(source) ? "hugo" : source;
            template.Description = description ?? String.Empty;
            _Templates[key] = template;
            BuiltInPrompts.Register(key, template.Content);
            return template;
        }

        /// <summary>
        /// Register the default instructions of connectors under their instruction keys.
        /// </summary>
        /// <param name="connectors">Connectors.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectors is null.</exception>
        public void RegisterConnectors(IEnumerable<IConnector> connectors)
        {
            if (connectors == null) throw new ArgumentNullException(nameof(connectors));
            foreach (IConnector connector in connectors)
            {
                ConnectorDescriptor d = connector.Descriptor;
                string content = String.IsNullOrWhiteSpace(d.DefaultInstructions)
                    ? "Use the " + d.Family + "_* tools with the resource argument set to this resource. " + d.Description
                    : d.DefaultInstructions;
                Register(d.DefaultInstructionsKey, content, d.Type, "Default instructions for " + (String.IsNullOrEmpty(d.DisplayName) ? d.Type : d.DisplayName) + " resources.");
                Register(d.CompactInstructionsKey, CompactContent(connector), d.Type, "Compact instructions for " + (String.IsNullOrEmpty(d.DisplayName) ? d.Type : d.DisplayName) + " resources, used when a profile has many resources.");
            }
        }

        /// <summary>
        /// Find a template.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Template or null.</returns>
        public BuiltInTemplate? Get(string? key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            return _Templates.TryGetValue(key, out BuiltInTemplate? template) ? template : null;
        }

        /// <summary>
        /// Every template, ordered by key.
        /// </summary>
        /// <returns>Templates.</returns>
        public List<BuiltInTemplate> All()
        {
            return _Templates.Values.OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        }

        #endregion

        private static string CompactContent(IConnector connector)
        {
            ConnectorDescriptor d = connector.Descriptor;
            if (!String.IsNullOrWhiteSpace(d.CompactInstructions)) return d.CompactInstructions;
            Assembly assembly = connector.GetType().Assembly;
            string suffix = "connector." + d.Type + ".compact.md";
            string? name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name != null) return EmbeddedText.Read(assembly, suffix);
            return "Use the " + d.Family + "_* tools with the resource argument set to this resource. Call hugo_describe_resource for full guidance before anything beyond simple reads.";
        }
    }
}
