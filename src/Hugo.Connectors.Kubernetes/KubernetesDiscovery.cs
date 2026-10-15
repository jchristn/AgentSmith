namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s;
    using k8s.Models;

    /// <summary>
    /// Resolves kind names (<c>Deployment</c>, <c>deploy</c>, <c>widgets.testbed.hugo.dev</c>) to API resource types
    /// using the API server's discovery documents, cached per session. Works for every built-in kind and every custom
    /// resource. Thread-safe.
    /// </summary>
    public class KubernetesDiscovery
    {
        #region Private-Members

        private readonly KubernetesSession _Session;
        private readonly ConcurrentDictionary<string, List<KubernetesResourceType>> _GroupVersions = new ConcurrentDictionary<string, List<KubernetesResourceType>>(StringComparer.OrdinalIgnoreCase);
        private List<V1APIGroup>? _Groups = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <exception cref="ArgumentNullException">Thrown when session is null.</exception>
        public KubernetesDiscovery(KubernetesSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve a kind or resource name to a type.
        /// </summary>
        /// <param name="name">Kind (<c>Deployment</c>), plural (<c>deployments</c>), short name (<c>deploy</c>), or
        /// <c>plural.group</c>.</param>
        /// <param name="apiVersion">apiVersion to constrain the lookup (<c>apps/v1</c>), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Type.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the name is empty and <c>not_found</c> when no served type matches.</exception>
        public async Task<KubernetesResourceType> ResolveAsync(string name, string? apiVersion, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "A kind is required, for example Pod, Deployment, or a custom resource kind.");
            string n = name.Trim();

            if (!String.IsNullOrWhiteSpace(apiVersion))
            {
                List<KubernetesResourceType> served = await GroupVersionAsync(apiVersion.Trim(), token).ConfigureAwait(false);
                KubernetesResourceType? inVersion = served.FirstOrDefault(t => t.Matches(n));
                if (inVersion != null) return inVersion;
                throw new ConnectorException(ConnectorErrorCodes.NotFound, "The API server does not serve kind '" + n + "' in " + apiVersion.Trim() + ".");
            }

            KubernetesResourceType? known = KubernetesKnownKinds.Find(n);
            if (known != null)
            {
                try
                {
                    List<KubernetesResourceType> served = await GroupVersionAsync(known.ApiVersion, token).ConfigureAwait(false);
                    KubernetesResourceType? match = served.FirstOrDefault(t => String.Equals(t.Kind, known.Kind, StringComparison.Ordinal));
                    if (match != null) return match;
                }
                catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.NotFound || e.Code == ConnectorErrorCodes.PermissionDenied)
                {
                    return known;
                }
            }

            List<V1APIGroup> groups = await GroupsAsync(token).ConfigureAwait(false);
            string? groupHint = null;
            int dot = n.IndexOf('.');
            if (dot > 0) groupHint = n.Substring(dot + 1);

            foreach (V1APIGroup group in groups)
            {
                if (group?.PreferredVersion == null || String.IsNullOrEmpty(group.PreferredVersion.GroupVersion)) continue;
                if (groupHint != null && !String.Equals(group.Name, groupHint, StringComparison.OrdinalIgnoreCase)) continue;
                List<KubernetesResourceType> served;
                try
                {
                    served = await GroupVersionAsync(group.PreferredVersion.GroupVersion, token).ConfigureAwait(false);
                }
                catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.NotFound || e.Code == ConnectorErrorCodes.PermissionDenied || e.Code == ConnectorErrorCodes.UpstreamError)
                {
                    continue;
                }
                KubernetesResourceType? match = served.FirstOrDefault(t => t.Matches(n));
                if (match != null) return match;
            }

            if (known != null) return known;
            throw new ConnectorException(ConnectorErrorCodes.NotFound, "No API resource named '" + n + "' is served by this cluster. Use the kind (for example Deployment), the plural (deployments), or plural.group for custom resources, and pass apiVersion when names collide.");
        }

        /// <summary>
        /// Every resource type served in a group version.
        /// </summary>
        /// <param name="groupVersion">Group version, for example <c>v1</c> or <c>apps/v1</c>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Types (top-level resources only; subresources are attached to their parent).</returns>
        /// <exception cref="ConnectorException">Thrown when the discovery document cannot be read.</exception>
        public async Task<List<KubernetesResourceType>> GroupVersionAsync(string groupVersion, CancellationToken token)
        {
            if (_GroupVersions.TryGetValue(groupVersion, out List<KubernetesResourceType>? cached)) return cached;
            string group = KubernetesKnownKinds.GroupOf(groupVersion);
            string version = KubernetesKnownKinds.VersionOf(groupVersion);
            string path = String.IsNullOrEmpty(group) ? "/api/" + version : "/apis/" + group + "/" + version;
            string body = await _Session.GetAsync(path, "API group version " + groupVersion, token).ConfigureAwait(false);
            V1APIResourceList? list;
            try
            {
                list = KubernetesJson.Deserialize<V1APIResourceList>(body);
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The discovery document for " + groupVersion + " could not be read.", e);
            }

            List<KubernetesResourceType> types = new List<KubernetesResourceType>();
            List<V1APIResource> resources = list?.Resources?.ToList() ?? new List<V1APIResource>();
            foreach (V1APIResource r in resources.Where(r => r != null && !String.IsNullOrEmpty(r.Name) && r.Name.IndexOf('/') < 0))
            {
                KubernetesResourceType t = new KubernetesResourceType();
                t.Group = group;
                t.Version = version;
                t.Kind = r.Kind ?? String.Empty;
                t.Plural = r.Name;
                t.Singular = String.IsNullOrEmpty(r.SingularName) ? (r.Kind ?? String.Empty).ToLowerInvariant() : r.SingularName;
                t.Namespaced = r.Namespaced;
                t.ShortNames = r.ShortNames?.ToList() ?? new List<string>();
                t.Verbs = r.Verbs?.ToList() ?? new List<string>();
                t.Subresources = resources.Where(s => s != null && s.Name != null && s.Name.StartsWith(r.Name + "/", StringComparison.Ordinal))
                    .Select(s => s.Name.Substring(r.Name.Length + 1))
                    .ToList();
                types.Add(t);
            }
            _GroupVersions[groupVersion] = types;
            return types;
        }

        /// <summary>
        /// Whether the API server serves a group version (for example <c>metrics.k8s.io/v1beta1</c>).
        /// </summary>
        /// <param name="groupVersion">Group version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when served.</returns>
        public async Task<bool> ServesAsync(string groupVersion, CancellationToken token)
        {
            try
            {
                await GroupVersionAsync(groupVersion, token).ConfigureAwait(false);
                return true;
            }
            catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.NotFound || e.Code == ConnectorErrorCodes.UpstreamError)
            {
                return false;
            }
        }

        /// <summary>
        /// Forget cached discovery documents (for example after a CRD was applied).
        /// </summary>
        public void Reset()
        {
            _GroupVersions.Clear();
            _Groups = null;
        }

        #endregion

        #region Private-Methods

        private async Task<List<V1APIGroup>> GroupsAsync(CancellationToken token)
        {
            List<V1APIGroup>? groups = _Groups;
            if (groups != null) return groups;
            string body = await _Session.GetAsync("/apis", "API group list", token).ConfigureAwait(false);
            V1APIGroupList? list;
            try
            {
                list = KubernetesJson.Deserialize<V1APIGroupList>(body);
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The API group list could not be read.", e);
            }
            groups = list?.Groups?.Where(g => g != null).ToList() ?? new List<V1APIGroup>();
            _Groups = groups;
            return groups;
        }

        #endregion
    }
}
