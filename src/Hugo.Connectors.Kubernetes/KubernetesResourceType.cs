namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One API resource type as discovered from the API server (or from the built-in table of well-known kinds): group,
    /// version, kind, plural, scope, verbs, and subresources. Immutable after construction by convention; thread-safe
    /// for reads.
    /// </summary>
    public class KubernetesResourceType
    {
        #region Public-Members

        /// <summary>
        /// API group. Empty for the core group.
        /// </summary>
        public string Group { get; set; } = String.Empty;

        /// <summary>
        /// API version, for example <c>v1</c>.
        /// </summary>
        public string Version { get; set; } = "v1";

        /// <summary>
        /// Kind, for example <c>Deployment</c>.
        /// </summary>
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Plural resource name, for example <c>deployments</c>.
        /// </summary>
        public string Plural { get; set; } = String.Empty;

        /// <summary>
        /// Singular resource name, for example <c>deployment</c>. May be empty.
        /// </summary>
        public string Singular { get; set; } = String.Empty;

        /// <summary>
        /// Whether objects of this type live in a namespace.
        /// </summary>
        public bool Namespaced { get; set; } = true;

        /// <summary>
        /// Short names, for example <c>deploy</c>. Never null.
        /// </summary>
        public List<string> ShortNames
        {
            get
            {
                return _ShortNames;
            }
            set
            {
                _ShortNames = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Supported verbs, for example <c>get</c>, <c>list</c>, <c>patch</c>. Never null. Empty when unknown.
        /// </summary>
        public List<string> Verbs
        {
            get
            {
                return _Verbs;
            }
            set
            {
                _Verbs = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Subresources, for example <c>scale</c> and <c>status</c>. Never null.
        /// </summary>
        public List<string> Subresources
        {
            get
            {
                return _Subresources;
            }
            set
            {
                _Subresources = value ?? new List<string>();
            }
        }

        /// <summary>
        /// The <c>apiVersion</c> string: <c>v1</c> for the core group, otherwise <c>group/version</c>.
        /// </summary>
        public string ApiVersion
        {
            get
            {
                return String.IsNullOrEmpty(Group) ? Version : Group + "/" + Version;
            }
        }

        /// <summary>
        /// Display name such as <c>deployments.apps</c> or <c>pods</c>.
        /// </summary>
        public string DisplayName
        {
            get
            {
                return String.IsNullOrEmpty(Group) ? Plural : Plural + "." + Group;
            }
        }

        #endregion

        #region Private-Members

        private List<string> _ShortNames = new List<string>();
        private List<string> _Verbs = new List<string>();
        private List<string> _Subresources = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a name (kind, plural, singular, short name, or <c>plural.group</c>) refers to this type. Case-insensitive.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True when it matches.</returns>
        public bool Matches(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            string n = name.Trim();
            if (String.Equals(n, Kind, StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(n, Plural, StringComparison.OrdinalIgnoreCase)) return true;
            if (!String.IsNullOrEmpty(Singular) && String.Equals(n, Singular, StringComparison.OrdinalIgnoreCase)) return true;
            if (!String.IsNullOrEmpty(Group) && String.Equals(n, Plural + "." + Group, StringComparison.OrdinalIgnoreCase)) return true;
            if (!String.IsNullOrEmpty(Group) && String.Equals(n, Kind + "." + Group, StringComparison.OrdinalIgnoreCase)) return true;
            return _ShortNames.Any(s => String.Equals(s, n, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether the type supports a verb. Unknown verb lists allow everything (the server decides).
        /// </summary>
        /// <param name="verb">Verb.</param>
        /// <returns>True when supported or unknown.</returns>
        public bool Supports(string verb)
        {
            if (_Verbs.Count == 0) return true;
            return _Verbs.Any(v => String.Equals(v, verb, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Build the REST path of the collection or of one object.
        /// </summary>
        /// <param name="ns">Namespace for namespaced types, or null for all namespaces.</param>
        /// <param name="name">Object name, or null for the collection.</param>
        /// <param name="subresource">Subresource such as <c>scale</c>, or null.</param>
        /// <returns>Path starting with <c>/api</c> or <c>/apis</c>.</returns>
        public string Path(string? ns, string? name, string? subresource = null)
        {
            string root = String.IsNullOrEmpty(Group) ? "/api/" + Version : "/apis/" + Group + "/" + Version;
            string path = root;
            if (Namespaced && !String.IsNullOrEmpty(ns)) path += "/namespaces/" + Uri.EscapeDataString(ns);
            path += "/" + Plural;
            if (!String.IsNullOrEmpty(name)) path += "/" + Uri.EscapeDataString(name);
            if (!String.IsNullOrEmpty(name) && !String.IsNullOrEmpty(subresource)) path += "/" + subresource;
            return path;
        }

        /// <summary>
        /// Create a copy.
        /// </summary>
        /// <returns>Copy.</returns>
        public KubernetesResourceType Clone()
        {
            return new KubernetesResourceType
            {
                Group = Group,
                Version = Version,
                Kind = Kind,
                Plural = Plural,
                Singular = Singular,
                Namespaced = Namespaced,
                ShortNames = new List<string>(_ShortNames),
                Verbs = new List<string>(_Verbs),
                Subresources = new List<string>(_Subresources)
            };
        }

        #endregion
    }
}
