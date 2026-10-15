namespace Hugo.Server.Services
{
    using System;
    using System.Reflection;
    using System.Runtime.Loader;

    /// <summary>
    /// A collectible load context for one connector plugin (plan section 8.2). The connector contract
    /// (Hugo.Connectors.Abstractions) and framework assemblies resolve from the host, so plugin types implement the
    /// host's IConnector; the plugin's other dependencies resolve from its own directory. Thread-safe.
    /// </summary>
    public class PluginLoadContext : AssemblyLoadContext
    {
        #region Private-Members

        private readonly AssemblyDependencyResolver _Resolver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Context name.</param>
        /// <param name="mainAssemblyPath">Path of the plugin's main assembly.</param>
        /// <exception cref="ArgumentNullException">Thrown when mainAssemblyPath is null or empty.</exception>
        public PluginLoadContext(string name, string mainAssemblyPath) : base(name, true)
        {
            if (String.IsNullOrEmpty(mainAssemblyPath)) throw new ArgumentNullException(nameof(mainAssemblyPath));
            _Resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Resolve a dependency: shared contract assemblies come from the host, the rest from the plugin directory.
        /// </summary>
        /// <param name="assemblyName">Assembly name.</param>
        /// <returns>The assembly, or null to defer to the default context.</returns>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName == null) return null;
            string? name = assemblyName.Name;
            if (String.Equals(name, "Hugo.Connectors.Abstractions", StringComparison.Ordinal)) return null;
            string? path = _Resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        /// <summary>
        /// Resolve a native library from the plugin directory.
        /// </summary>
        /// <param name="unmanagedDllName">Library name.</param>
        /// <returns>Handle, or zero to defer.</returns>
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _Resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }

        #endregion
    }
}
