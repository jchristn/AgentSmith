// Ported from Garrison at 693ab8ece3dfba3839f2e4bcc1c4bb17745bfa2c (MIT); namespace rename only.
namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;

    /// <summary>
    /// Metric cardinality guard (P13.21): remembers the distinct values seen for each label key and, once a key has
    /// <see cref="MaxLabelValues"/> of them, reports any new value as <c>other</c> and calls the fold callback. Values
    /// already seen keep their own series. Empty values become <c>none</c>. Thread-safe; the limit may be exceeded by a
    /// few values under heavy concurrency.
    /// </summary>
    public class LabelGuard
    {
        #region Public-Members

        /// <summary>
        /// Distinct values kept per label key before new values fold into <c>other</c>. Minimum 10, maximum 10000,
        /// default 200.
        /// </summary>
        public int MaxLabelValues
        {
            get
            {
                return _MaxLabelValues;
            }
            set
            {
                _MaxLabelValues = Math.Clamp(value, 10, 10000);
            }
        }

        /// <summary>
        /// Invoked with the label key each time a value is folded. Null for none. Must not throw.
        /// </summary>
        public Action<string>? OnFolded { get; set; } = null;

        /// <summary>
        /// Value reported for folded label values: other.
        /// </summary>
        public string FoldedValue { get; } = "other";

        /// <summary>
        /// Value reported for empty or null label values: none.
        /// </summary>
        public string EmptyValue { get; } = "none";

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _Seen = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal);
        private int _MaxLabelValues = 200;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The value to report for a label.
        /// </summary>
        /// <param name="key">Label key.</param>
        /// <param name="value">Raw value, or null.</param>
        /// <returns>The value, <c>none</c> when empty, or <c>other</c> when the key is full.</returns>
        public string Fold(string key, string? value)
        {
            if (String.IsNullOrEmpty(value)) return EmptyValue;
            if (String.IsNullOrEmpty(key)) return value;
            ConcurrentDictionary<string, byte> seen = _Seen.GetOrAdd(key, k => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            if (seen.ContainsKey(value)) return value;
            if (seen.Count < _MaxLabelValues)
            {
                seen.TryAdd(value, 0);
                return value;
            }
            try
            {
                OnFolded?.Invoke(key);
            }
            catch (Exception)
            {
            }
            return FoldedValue;
        }

        /// <summary>
        /// Number of distinct values remembered for a label key.
        /// </summary>
        /// <param name="key">Label key.</param>
        /// <returns>Count.</returns>
        public int Count(string key)
        {
            if (String.IsNullOrEmpty(key)) return 0;
            return _Seen.TryGetValue(key, out ConcurrentDictionary<string, byte>? seen) ? seen.Count : 0;
        }

        /// <summary>
        /// Forget every remembered value (tests).
        /// </summary>
        public void Reset()
        {
            _Seen.Clear();
        }

        #endregion
    }
}
