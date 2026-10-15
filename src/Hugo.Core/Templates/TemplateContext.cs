namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Typed render context: a tree of case-insensitive dictionaries, lists, and scalar values (string, bool, numbers,
    /// DateTime). Templates never reflect over arbitrary objects. Not thread-safe for writes.
    /// </summary>
    public class TemplateContext
    {
        #region Public-Members

        /// <summary>
        /// Root dictionary.
        /// </summary>
        public Dictionary<string, object?> Root
        {
            get
            {
                return _Root;
            }
        }

        #endregion

        #region Private-Members

        private readonly Dictionary<string, object?> _Root = NewMap();

        #endregion

        #region Public-Methods

        /// <summary>
        /// New case-insensitive map for nested context values.
        /// </summary>
        /// <returns>Map.</returns>
        public static Dictionary<string, object?> NewMap()
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Set a value at a dotted path, creating intermediate maps.
        /// </summary>
        /// <param name="path">Dotted path.</param>
        /// <param name="value">Value.</param>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        public void Set(string path, object? value)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            string[] segments = path.Split('.');
            Dictionary<string, object?> current = _Root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (!current.TryGetValue(segments[i], out object? next) || next is not Dictionary<string, object?> map)
                {
                    map = NewMap();
                    current[segments[i]] = map;
                }
                current = map;
            }
            current[segments[segments.Length - 1]] = value;
        }

        /// <summary>
        /// Resolve a dotted path from the root.
        /// </summary>
        /// <param name="path">Dotted path.</param>
        /// <returns>Value or null.</returns>
        public object? Get(string path)
        {
            return Resolve(_Root, path);
        }

        /// <summary>
        /// Resolve a dotted path from a starting value.
        /// </summary>
        /// <param name="start">Starting value.</param>
        /// <param name="path">Dotted path (empty returns start).</param>
        /// <returns>Value or null.</returns>
        public static object? Resolve(object? start, string? path)
        {
            if (String.IsNullOrEmpty(path)) return start;
            object? current = start;
            foreach (string segment in path.Split('.'))
            {
                if (current is Dictionary<string, object?> map)
                {
                    if (!map.TryGetValue(segment, out current)) return null;
                }
                else if (current is List<object?> list && segment.Equals("count", StringComparison.OrdinalIgnoreCase))
                {
                    current = list.Count;
                }
                else
                {
                    return null;
                }
            }
            return current;
        }

        /// <summary>
        /// Shallow copy of the root so a caller can add values without affecting the original.
        /// </summary>
        /// <returns>Copy.</returns>
        public TemplateContext Clone()
        {
            TemplateContext copy = new TemplateContext();
            foreach (KeyValuePair<string, object?> entry in _Root) copy._Root[entry.Key] = entry.Value;
            return copy;
        }

        #endregion
    }
}
