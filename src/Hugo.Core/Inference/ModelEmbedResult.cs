namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of a gateway embedding call.
    /// </summary>
    public class ModelEmbedResult
    {
        #region Public-Members

        /// <summary>
        /// One vector per input, in input order. Never null.
        /// </summary>
        public List<float[]> Vectors
        {
            get
            {
                return _Vectors;
            }
            set
            {
                _Vectors = value ?? new List<float[]>();
            }
        }

        /// <summary>
        /// Vector dimensions (0 when no vectors).
        /// </summary>
        public int Dimensions { get; set; } = 0;

        /// <summary>
        /// Milliseconds for the whole gateway call.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Key of the endpoint that answered.
        /// </summary>
        public string EndpointKey { get; set; } = String.Empty;

        /// <summary>
        /// Whether the fallback endpoint answered.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        #endregion

        #region Private-Members

        private List<float[]> _Vectors = new List<float[]>();

        #endregion
    }
}
