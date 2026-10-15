namespace Hugo.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// Registry of built-in prompt template defaults by key, used by Reset to Default. Phase 5 (plan section 10.2) fills it
    /// from embedded resource files; until then it is empty and reset answers that no default exists. Thread-safe.
    /// </summary>
    public static class BuiltInPrompts
    {
        #region Private-Members

        private static readonly ConcurrentDictionary<string, string> _Defaults = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Shipped default content for a built-in key.
        /// </summary>
        /// <param name="key">Built-in key, or null.</param>
        /// <returns>Content, or null when the key has no default.</returns>
        public static string? Default(string? key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            return _Defaults.TryGetValue(key, out string? content) ? content : null;
        }

        /// <summary>
        /// Register (or replace) a built-in default.
        /// </summary>
        /// <param name="key">Built-in key.</param>
        /// <param name="content">Content.</param>
        /// <exception cref="ArgumentNullException">Thrown when key or content is null.</exception>
        public static void Register(string key, string content)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            _Defaults[key] = content ?? throw new ArgumentNullException(nameof(content));
        }

        /// <summary>
        /// Every registered key.
        /// </summary>
        /// <returns>Keys.</returns>
        public static List<string> Keys()
        {
            return new List<string>(_Defaults.Keys);
        }

        #endregion
    }
}
