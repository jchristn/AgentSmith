namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Plaintext secret material for one invocation, keyed by slot name and field name. Exists only for the duration of
    /// a call; <see cref="Dispose()"/> clears it. Connectors must never log, return, or persist values read here.
    /// Thread-safe for reads.
    /// </summary>
    public class SecretAccessor : IDisposable
    {
        #region Private-Members

        private readonly Dictionary<string, Dictionary<string, string>> _Slots;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty accessor.
        /// </summary>
        public SecretAccessor()
        {
            _Slots = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Instantiate from slot to field to value maps.
        /// </summary>
        /// <param name="slots">Values. Null is treated as empty.</param>
        public SecretAccessor(IDictionary<string, Dictionary<string, string>>? slots)
        {
            _Slots = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (slots == null) return;
            foreach (KeyValuePair<string, Dictionary<string, string>> slot in slots)
                _Slots[slot.Key] = new Dictionary<string, string>(slot.Value ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a secret is bound to the slot.
        /// </summary>
        /// <param name="slot">Slot name.</param>
        /// <returns>True when bound.</returns>
        public bool HasSlot(string slot)
        {
            ThrowIfDisposed();
            return !String.IsNullOrEmpty(slot) && _Slots.ContainsKey(slot);
        }

        /// <summary>
        /// Read a field of the secret bound to a slot.
        /// </summary>
        /// <param name="slot">Slot name.</param>
        /// <param name="field">Field name (for example <c>password</c>).</param>
        /// <returns>The value, or null when the slot or field is absent.</returns>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        public string? Get(string slot, string field)
        {
            ThrowIfDisposed();
            if (String.IsNullOrEmpty(slot) || String.IsNullOrEmpty(field)) return null;
            if (!_Slots.TryGetValue(slot, out Dictionary<string, string>? fields)) return null;
            return fields.TryGetValue(field, out string? value) ? value : null;
        }

        /// <summary>
        /// All plaintext values, used by the server's scrubber to redact them from outputs.
        /// </summary>
        /// <returns>Values.</returns>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        public List<string> AllValues()
        {
            ThrowIfDisposed();
            List<string> values = new List<string>();
            foreach (Dictionary<string, string> fields in _Slots.Values)
                foreach (string value in fields.Values)
                    if (!String.IsNullOrEmpty(value)) values.Add(value);
            return values;
        }

        /// <summary>
        /// Clear all values.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Clear all values.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                foreach (Dictionary<string, string> fields in _Slots.Values) fields.Clear();
                _Slots.Clear();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void ThrowIfDisposed()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(SecretAccessor));
        }

        #endregion
    }
}
