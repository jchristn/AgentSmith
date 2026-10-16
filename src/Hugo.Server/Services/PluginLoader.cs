namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Connector plugin loader (P3.19, plan section 8.2): each subdirectory of the plugin directory holds one plugin
    /// whose main assembly is named after the directory (plugins/acme/acme.dll), loaded into its own collectible
    /// <see cref="PluginLoadContext"/>. Public types implementing IConnector with a public parameterless constructor are
    /// registered. A type that duplicates a registered connector type refuses the whole plugin. Every load, refusal, and
    /// unload is recorded in the plugins table and audited (PluginLoaded). Off unless Connectors.EnablePlugins is true.
    /// Thread-safe.
    /// </summary>
    public class PluginLoader : IDisposable
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly ConnectorRegistry _Registry;
        private readonly AuditService _Audit;
        private readonly HugoLog _Logging;
        private readonly ConcurrentDictionary<string, PluginLoadContext> _Contexts = new ConcurrentDictionary<string, PluginLoadContext>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, List<string>> _Types = new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="registry">Connector registry.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public PluginLoader(DatabaseDriverBase database, ConnectorRegistry registry, AuditService audit, HugoLog logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Connector types loaded from plugins.
        /// </summary>
        /// <returns>Types.</returns>
        public List<string> LoadedTypes()
        {
            return _Types.Values.SelectMany(t => t).OrderBy(t => t, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Load every plugin in a directory.
        /// </summary>
        /// <param name="directory">Plugin directory.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Plugin records (loaded or refused).</returns>
        public async Task<List<PluginRecord>> LoadAllAsync(string directory, CancellationToken token = default)
        {
            List<PluginRecord> records = new List<PluginRecord>();
            if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return records;
            foreach (string pluginDirectory in Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                records.Add(await LoadAsync(pluginDirectory, token).ConfigureAwait(false));
            }
            return records;
        }

        /// <summary>
        /// Load one plugin directory.
        /// </summary>
        /// <param name="pluginDirectory">Directory holding &lt;name&gt;.dll.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record (Loaded true, or LoadError set).</returns>
        public async Task<PluginRecord> LoadAsync(string pluginDirectory, CancellationToken token = default)
        {
            string name = Path.GetFileName(pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            PluginRecord record = await _Database.Plugins.ReadByNameAsync(name, token).ConfigureAwait(false) ?? new PluginRecord();
            record.Name = name;
            record.AssemblyPath = Path.GetFullPath(Path.Combine(pluginDirectory, name + ".dll"));
            record.Loaded = false;
            record.LoadError = null;
            record.ConnectorTypes = new List<string>();
            if (!record.Enabled)
            {
                record.LoadError = "Disabled.";
                return await SaveAsync(record, "Plugin " + name + " is disabled and was not loaded.", token).ConfigureAwait(false);
            }
            if (_Contexts.ContainsKey(name))
            {
                record.Loaded = true;
                record.ConnectorTypes = _Types.TryGetValue(name, out List<string>? types) ? new List<string>(types) : new List<string>();
                return record;
            }
            if (!File.Exists(record.AssemblyPath))
            {
                record.LoadError = "Assembly " + name + ".dll was not found in " + pluginDirectory + ".";
                return await SaveAsync(record, "Plugin " + name + " refused: " + record.LoadError, token).ConfigureAwait(false);
            }
            using (FileStream stream = File.OpenRead(record.AssemblyPath))
            {
                record.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
            }
            PluginLoadContext context = new PluginLoadContext("hugo-plugin-" + name, record.AssemblyPath);
            List<IConnector> connectors = new List<IConnector>();
            try
            {
                Assembly assembly = context.LoadFromAssemblyPath(record.AssemblyPath);
                record.Version = assembly.GetName().Version?.ToString();
                foreach (Type type in assembly.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(IConnector).IsAssignableFrom(t)))
                {
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    IConnector connector = (IConnector)Activator.CreateInstance(type)!;
                    if (_Registry.Get(connector.Descriptor.Type) != null || connectors.Any(c => c.Descriptor.Type == connector.Descriptor.Type))
                        throw new InvalidOperationException("Connector type '" + connector.Descriptor.Type + "' is already registered.");
                    connectors.Add(connector);
                }
                if (connectors.Count == 0) throw new InvalidOperationException("No public IConnector type with a parameterless constructor was found.");
                List<string> registered = new List<string>();
                try
                {
                    foreach (IConnector connector in connectors)
                    {
                        _Registry.Register(connector);
                        registered.Add(connector.Descriptor.Type);
                    }
                }
                catch
                {
                    foreach (string type in registered) _Registry.Unregister(type);
                    throw;
                }
                _Contexts[name] = context;
                _Types[name] = registered;
                record.Loaded = true;
                record.ConnectorTypes = registered;
                _Logging.Info("[Plugins] loaded " + name + " (" + String.Join(", ", registered) + ")");
                return await SaveAsync(record, "Loaded plugin " + name + " " + (record.Version ?? String.Empty) + " (sha256 " + record.Sha256 + ") with connector types " + String.Join(", ", registered) + ".", token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                context.Unload();
                record.LoadError = e is ReflectionTypeLoadException || e is FileLoadException || e is BadImageFormatException ? "The assembly could not be loaded: " + e.Message : e.Message;
                _Logging.Warn("[Plugins] refused " + name + ": " + record.LoadError);
                return await SaveAsync(record, "Plugin " + name + " refused: " + record.LoadError, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Unregister a plugin's connectors and unload its context.
        /// </summary>
        /// <param name="name">Plugin name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it was loaded.</returns>
        public async Task<bool> UnloadAsync(string name, CancellationToken token = default)
        {
            if (!_Contexts.TryRemove(name, out PluginLoadContext? context)) return false;
            if (_Types.TryRemove(name, out List<string>? types))
            {
                foreach (string type in types) _Registry.Unregister(type);
            }
            context.Unload();
            PluginRecord? record = await _Database.Plugins.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (record != null)
            {
                record.Loaded = false;
                await SaveAsync(record, "Unloaded plugin " + name + ".", token).ConfigureAwait(false);
            }
            return true;
        }

        /// <summary>
        /// Unload every plugin.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Unregister plugin connectors and unload contexts.
        /// </summary>
        /// <param name="disposing">Whether called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                foreach (string name in _Contexts.Keys.ToList())
                {
                    if (_Contexts.TryRemove(name, out PluginLoadContext? context))
                    {
                        if (_Types.TryRemove(name, out List<string>? types)) foreach (string type in types) _Registry.Unregister(type);
                        context.Unload();
                    }
                }
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<PluginRecord> SaveAsync(PluginRecord record, string description, CancellationToken token)
        {
            PluginRecord saved = await _Database.Plugins.UpsertAsync(record, token).ConfigureAwait(false);
            await _Audit.WriteAsync(null, AuditEventTypeEnum.PluginLoaded, "Plugin", saved.Id, description, null, token).ConfigureAwait(false);
            return saved;
        }

        #endregion
    }
}
