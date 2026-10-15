namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Registry of connectors keyed by type (plan P3.3). Enforces unique types, tool naming, and the family rule: tools
    /// with the same name in one family must share an input schema. Thread-safe: registration takes a write lock and
    /// reads take a read lock.
    /// </summary>
    public class ConnectorRegistry : IDisposable
    {
        #region Private-Members

        private static readonly Regex _ToolName = new Regex("^[a-z][a-z0-9]*(_[a-z0-9]+)+$", RegexOptions.CultureInvariant);
        private static readonly Regex _TypeName = new Regex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
        private readonly Dictionary<string, IConnector> _Connectors = new Dictionary<string, IConnector>(StringComparer.Ordinal);
        private readonly System.Threading.ReaderWriterLockSlim _Lock = new System.Threading.ReaderWriterLockSlim();
        private bool _Disposed = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a connector.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <exception cref="ArgumentNullException">Thrown when connector is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the type is taken, a tool name is invalid or duplicated,
        /// or a same-named family tool has a different input schema.</exception>
        public void Register(IConnector connector)
        {
            ArgumentNullException.ThrowIfNull(connector);
            ConnectorDescriptor d = connector.Descriptor ?? throw new InvalidOperationException("Connector " + connector.GetType().FullName + " has no descriptor.");
            if (!_TypeName.IsMatch(d.Type)) throw new InvalidOperationException("Connector type '" + d.Type + "' must be lowercase letters and digits separated by hyphens.");

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (IConnectorTool tool in connector.Tools ?? new List<IConnectorTool>())
            {
                string name = tool.Descriptor.Name;
                if (!_ToolName.IsMatch(name)) throw new InvalidOperationException("Tool '" + name + "' of connector '" + d.Type + "' must be snake_case family_operation.");
                if (!names.Add(name)) throw new InvalidOperationException("Connector '" + d.Type + "' declares tool '" + name + "' twice.");
                ValidateSchema(d.Type, tool.Descriptor);
            }

            _Lock.EnterWriteLock();
            try
            {
                if (_Connectors.ContainsKey(d.Type)) throw new InvalidOperationException("Connector type '" + d.Type + "' is already registered.");
                foreach (IConnector other in _Connectors.Values.Where(c => String.Equals(c.Descriptor.Family, d.Family, StringComparison.Ordinal)))
                {
                    foreach (IConnectorTool tool in connector.Tools ?? new List<IConnectorTool>())
                    {
                        IConnectorTool? peer = other.Tools.FirstOrDefault(t => String.Equals(t.Descriptor.Name, tool.Descriptor.Name, StringComparison.Ordinal));
                        if (peer == null) continue;
                        if (!SchemasEqual(peer.Descriptor.InputSchemaJson, tool.Descriptor.InputSchemaJson))
                            throw new InvalidOperationException("Tool '" + tool.Descriptor.Name + "' in family '" + d.Family + "' has a different input schema in connectors '" + other.Descriptor.Type + "' and '" + d.Type + "'.");
                    }
                }
                _Connectors[d.Type] = connector;
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Remove a connector (used when a plugin unloads).
        /// </summary>
        /// <param name="type">Connector type.</param>
        /// <returns>True when removed.</returns>
        public bool Unregister(string type)
        {
            if (String.IsNullOrEmpty(type)) return false;
            _Lock.EnterWriteLock();
            try
            {
                return _Connectors.Remove(type);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Find a connector by type.
        /// </summary>
        /// <param name="type">Connector type.</param>
        /// <returns>The connector, or null.</returns>
        public IConnector? Get(string type)
        {
            if (String.IsNullOrEmpty(type)) return null;
            _Lock.EnterReadLock();
            try
            {
                return _Connectors.TryGetValue(type, out IConnector? c) ? c : null;
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// All connectors ordered by category then type.
        /// </summary>
        /// <returns>Connectors.</returns>
        public List<IConnector> All()
        {
            _Lock.EnterReadLock();
            try
            {
                return _Connectors.Values.OrderBy(c => c.Descriptor.Category).ThenBy(c => c.Descriptor.Type, StringComparer.Ordinal).ToList();
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Find a tool on a connector.
        /// </summary>
        /// <param name="type">Connector type.</param>
        /// <param name="toolName">Tool name.</param>
        /// <returns>The tool, or null.</returns>
        public IConnectorTool? FindTool(string type, string toolName)
        {
            IConnector? c = Get(type);
            if (c == null || String.IsNullOrEmpty(toolName)) return null;
            return c.Tools.FirstOrDefault(t => String.Equals(t.Descriptor.Name, toolName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Tools of a family, one entry per tool name, with the connector types offering each.
        /// </summary>
        /// <param name="family">Family.</param>
        /// <returns>Family tools ordered by name.</returns>
        public List<FamilyTool> FamilyTools(string family)
        {
            Dictionary<string, FamilyTool> tools = new Dictionary<string, FamilyTool>(StringComparer.Ordinal);
            foreach (IConnector c in All().Where(c => String.Equals(c.Descriptor.Family, family, StringComparison.Ordinal)))
            {
                foreach (IConnectorTool t in c.Tools)
                {
                    if (!tools.TryGetValue(t.Descriptor.Name, out FamilyTool? ft))
                    {
                        ft = new FamilyTool { Family = family, Descriptor = t.Descriptor };
                        tools[t.Descriptor.Name] = ft;
                    }
                    ft.ConnectorTypes.Add(c.Descriptor.Type);
                }
            }
            return tools.Values.OrderBy(t => t.Descriptor.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Release the lock.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Release the lock.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Lock.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private static void ValidateSchema(string type, ToolDescriptor tool)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(tool.InputSchemaJson))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Tool '" + tool.Name + "' of connector '" + type + "' has an input schema that is not a JSON object.");
                    if (doc.RootElement.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Object && props.TryGetProperty("resource", out JsonElement _))
                        throw new InvalidOperationException("Tool '" + tool.Name + "' of connector '" + type + "' declares the reserved 'resource' argument.");
                }
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException("Tool '" + tool.Name + "' of connector '" + type + "' has an invalid input schema: " + e.Message, e);
            }
        }

        private static bool SchemasEqual(string a, string b)
        {
            return Canonical(a) == Canonical(b);
        }

        private static string Canonical(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                return JsonSerializer.Serialize(doc.RootElement);
            }
        }

        #endregion
    }
}
