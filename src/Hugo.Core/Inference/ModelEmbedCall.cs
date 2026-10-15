namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One embedding request to the gateway. Not thread-safe; build one per call.
    /// </summary>
    public class ModelEmbedCall
    {
        #region Public-Members

        /// <summary>
        /// Embedding endpoint. Required.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public ModelEndpointConfig Endpoint
        {
            get
            {
                return _Endpoint;
            }
            set
            {
                _Endpoint = value ?? throw new ArgumentNullException(nameof(Endpoint));
            }
        }

        /// <summary>
        /// Fallback embedding endpoint. Only useful when it produces vectors in the same space (same model). Null
        /// for none.
        /// </summary>
        public ModelEndpointConfig? Fallback { get; set; } = null;

        /// <summary>
        /// Texts to embed. Never null; must not be empty when sent.
        /// </summary>
        public List<string> Inputs
        {
            get
            {
                return _Inputs;
            }
            set
            {
                _Inputs = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private ModelEndpointConfig _Endpoint = new ModelEndpointConfig { Kind = ModelKindEnum.Embedding };
        private List<string> _Inputs = new List<string>();

        #endregion
    }
}
