namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Concurrent;
    using System.Security.Cryptography;
    using System.Text;
    using Hugo.Core.Inference;

    /// <summary>
    /// Embeddings of tool descriptions, keyed by a hash of the embedding endpoint and model plus the tool's name
    /// and description, so a changed description is re-embedded and different models never share vectors.
    /// Rebuildable in-process cache (D-027). When it exceeds <see cref="MaxEntries"/> it is cleared. Thread-safe.
    /// </summary>
    public class ToolEmbeddingCache
    {
        #region Public-Members

        /// <summary>
        /// Maximum cached vectors before the cache is cleared. Default 20,000. Minimum 1, maximum 10,000,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int MaxEntries
        {
            get
            {
                return _MaxEntries;
            }
            set
            {
                if (value < 1 || value > 10_000_000) throw new ArgumentOutOfRangeException(nameof(MaxEntries), "MaxEntries must be between 1 and 10,000,000.");
                _MaxEntries = value;
            }
        }

        /// <summary>
        /// Cached vectors.
        /// </summary>
        public int Count
        {
            get
            {
                return _Vectors.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, float[]> _Vectors = new ConcurrentDictionary<string, float[]>(StringComparer.Ordinal);
        private int _MaxEntries = 20_000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Cache key for a tool under an embedding endpoint.
        /// </summary>
        /// <param name="endpoint">Embedding endpoint.</param>
        /// <param name="tool">Tool.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string KeyFor(ModelEndpointConfig endpoint, AgentToolDefinition tool)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(tool);
            string material = endpoint.Key + "\n" + endpoint.Model + "\n" + tool.Name + "\n" + tool.Description;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }

        /// <summary>
        /// Text embedded for a tool.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <returns>Text.</returns>
        public static string TextFor(AgentToolDefinition tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            return tool.Name.Replace('_', ' ') + ": " + tool.Description;
        }

        /// <summary>
        /// Try to get a cached vector.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="vector">Vector when found.</param>
        /// <returns>True when found.</returns>
        public bool TryGet(string key, out float[]? vector)
        {
            if (key != null && _Vectors.TryGetValue(key, out float[]? found))
            {
                vector = found;
                return true;
            }
            vector = null;
            return false;
        }

        /// <summary>
        /// Store a vector.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="vector">Vector.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public void Set(string key, float[] vector)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(vector);
            if (_Vectors.Count >= _MaxEntries) _Vectors.Clear();
            _Vectors[key] = vector;
        }

        /// <summary>
        /// Remove every vector.
        /// </summary>
        public void Clear()
        {
            _Vectors.Clear();
        }

        #endregion
    }
}
