namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Include cycle detection for templates that include each other with <c>{{> key}}</c>, used when a template is saved
    /// so a template that includes itself, directly or through others, is refused instead of failing at render time.
    /// Thread-safe.
    /// </summary>
    public static class TemplateIncludeGraph
    {
        #region Public-Members

        /// <summary>
        /// Deepest include chain followed. Minimum 1, maximum 256, default 32.
        /// </summary>
        public static int MaxDepth
        {
            get
            {
                return _MaxDepth;
            }
            set
            {
                _MaxDepth = Math.Clamp(value, 1, 256);
            }
        }

        #endregion

        #region Private-Members

        private static int _MaxDepth = 32;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find an include cycle reachable from a template that is about to be saved.
        /// </summary>
        /// <param name="key">Key (or name) of the template being saved.</param>
        /// <param name="content">The new content of that template.</param>
        /// <param name="resolve">Returns the current content of another template by key, or null when it does not exist.
        /// Unresolvable includes are not cycles.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The cycle as "a > b > a", or null when there is none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when key or resolve is null.</exception>
        public static async Task<string?> FindCycleAsync(string key, string? content, Func<string, CancellationToken, Task<string?>> resolve, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            Dictionary<string, List<string>> includes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            includes[key] = TemplateAnalyzer.Analyze(content ?? String.Empty, key).Includes;
            List<string> path = new List<string> { key };
            return await VisitAsync(key, key, path, includes, resolve, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static async Task<string?> VisitAsync(string start, string current, List<string> path, Dictionary<string, List<string>> includes, Func<string, CancellationToken, Task<string?>> resolve, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (path.Count > _MaxDepth) return null;
            foreach (string next in await IncludesAsync(current, includes, resolve, token).ConfigureAwait(false))
            {
                if (path.Contains(next, StringComparer.Ordinal))
                {
                    if (String.Equals(next, start, StringComparison.Ordinal)) return String.Join(" > ", path) + " > " + next;
                    continue;
                }
                path.Add(next);
                string? found = await VisitAsync(start, next, path, includes, resolve, token).ConfigureAwait(false);
                path.RemoveAt(path.Count - 1);
                if (found != null) return found;
            }
            return null;
        }

        private static async Task<List<string>> IncludesAsync(string key, Dictionary<string, List<string>> includes, Func<string, CancellationToken, Task<string?>> resolve, CancellationToken token)
        {
            if (includes.TryGetValue(key, out List<string>? known)) return known;
            string? content = await resolve(key, token).ConfigureAwait(false);
            List<string> found = content == null ? new List<string>() : TemplateAnalyzer.Analyze(content, key).Includes;
            includes[key] = found;
            return found;
        }

        #endregion
    }
}
