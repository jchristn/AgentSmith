namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Reads embedded text resources (for example default instructions). Thread-safe.
    /// </summary>
    public static class EmbeddedText
    {
        /// <summary>
        /// Read an embedded resource whose manifest name ends with the given suffix.
        /// </summary>
        /// <param name="assembly">Assembly containing the resource.</param>
        /// <param name="nameSuffix">Suffix, for example <c>connector.ssh.instructions.md</c>.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when no resource matches.</exception>
        public static string Read(Assembly assembly, string nameSuffix)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            if (String.IsNullOrEmpty(nameSuffix)) throw new ArgumentNullException(nameof(nameSuffix));
            string? name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(nameSuffix, StringComparison.OrdinalIgnoreCase));
            if (name == null) throw new FileNotFoundException("Embedded resource ending with '" + nameSuffix + "' was not found in " + assembly.GetName().Name + ".");
            using (Stream? stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null) throw new FileNotFoundException("Embedded resource '" + name + "' could not be opened.");
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
