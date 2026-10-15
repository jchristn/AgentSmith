namespace Test.Shared.Agent.StubLlm
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Deterministic feature-hashing embedder: lowercase word tokens (with a trailing plural <c>s</c> removed) are
    /// hashed with FNV-1a into <see cref="Dimensions"/> signed buckets, then the vector is L2-normalized. Texts that
    /// share words get high cosine similarity, so tool retrieval behaves predictably without a real model. The same
    /// text always yields the same vector on every platform. Thread-safe (stateless apart from configuration).
    /// </summary>
    public class FeatureHashEmbedder
    {
        #region Public-Members

        /// <summary>
        /// Vector dimensions. Default 256. Minimum 8, maximum 65,536.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int Dimensions
        {
            get
            {
                return _Dimensions;
            }
            set
            {
                if (value < 8 || value > 65536) throw new ArgumentOutOfRangeException(nameof(Dimensions), "Dimensions must be between 8 and 65,536.");
                _Dimensions = value;
            }
        }

        /// <summary>
        /// Words ignored when hashing. Never null.
        /// </summary>
        public HashSet<string> StopWords { get; set; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "an", "the", "of", "on", "in", "to", "for", "and", "or", "with", "by", "is", "it", "this", "that", "me", "my", "please", "what", "from", "against", "use"
        };

        #endregion

        #region Private-Members

        private int _Dimensions = 256;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Embed a text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Unit vector (all zeros for text with no tokens).</returns>
        public float[] Embed(string? text)
        {
            float[] vector = new float[_Dimensions];
            foreach (string token in Tokenize(text ?? String.Empty))
            {
                uint hash = Fnv1a(token);
                int index = (int)(hash % (uint)_Dimensions);
                float sign = ((hash >> 31) & 1) == 1 ? -1f : 1f;
                vector[index] += sign;
            }
            double norm = 0;
            for (int i = 0; i < vector.Length; i++) norm += vector[i] * vector[i];
            if (norm > 0)
            {
                float inv = (float)(1.0 / Math.Sqrt(norm));
                for (int i = 0; i < vector.Length; i++) vector[i] *= inv;
            }
            return vector;
        }

        /// <summary>
        /// Tokens used for hashing.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Tokens.</returns>
        public List<string> Tokenize(string text)
        {
            List<string> tokens = new List<string>();
            StringBuilder current = new StringBuilder();
            foreach (char c in (text ?? String.Empty).ToLowerInvariant())
            {
                if (Char.IsLetterOrDigit(c))
                {
                    current.Append(c);
                }
                else
                {
                    Flush(current, tokens);
                }
            }
            Flush(current, tokens);
            return tokens;
        }

        #endregion

        #region Private-Methods

        private void Flush(StringBuilder current, List<string> tokens)
        {
            if (current.Length == 0) return;
            string token = current.ToString();
            current.Clear();
            if (token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal)) token = token.Substring(0, token.Length - 1);
            if (StopWords.Contains(token)) return;
            tokens.Add(token);
        }

        private static uint Fnv1a(string s)
        {
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                hash ^= b;
                hash *= 16777619;
            }
            return hash;
        }

        #endregion
    }
}
