namespace Hugo.Cli
{
    using System;
    using System.Reflection;

    /// <summary>
    /// Product name and version of the hugo command line. Thread-safe.
    /// </summary>
    public static class ProductInfo
    {
        /// <summary>
        /// The product version from the assembly's informational version, without any source revision suffix (for
        /// example <c>0.1.0-alpha</c>).
        /// </summary>
        public static string Version
        {
            get
            {
                Assembly assembly = typeof(ProductInfo).Assembly;
                AssemblyInformationalVersionAttribute? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string version = info?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
                int plus = version.IndexOf('+', StringComparison.Ordinal);
                return plus > 0 ? version.Substring(0, plus) : version;
            }
        }

        /// <summary>
        /// The User-Agent value sent with every request (for example <c>hugo-cli/0.1.0-alpha</c>).
        /// </summary>
        public static string UserAgent
        {
            get
            {
                return "hugo-cli/" + Version;
            }
        }
    }
}
