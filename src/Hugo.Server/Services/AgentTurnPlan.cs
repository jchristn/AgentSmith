namespace Hugo.Server.Services
{
    using System;
    using Hugo.Core.Inference;

    /// <summary>
    /// A prepared turn: the conversation is claimed, the endpoints are resolved, and the instructions are compiled, so
    /// every error a client should see as an HTTP status has already happened. Run it with
    /// <see cref="AgentTurnHost.RunAsync(AgentTurnPlan, System.Threading.CancellationToken)"/>, then dispose it to release
    /// the conversation. Not thread-safe.
    /// </summary>
    public class AgentTurnPlan : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Options.
        /// </summary>
        public AgentTurnOptions Options { get; }

        /// <summary>
        /// The claimed conversation (cancel through <see cref="ActiveTurnRegistry"/>).
        /// </summary>
        public ActiveTurn Turn { get; }

        /// <summary>
        /// Inference endpoint.
        /// </summary>
        public ModelEndpointConfig Inference { get; set; } = new ModelEndpointConfig();

        /// <summary>
        /// Fallback inference endpoint, or null.
        /// </summary>
        public ModelEndpointConfig? Fallback { get; set; } = null;

        /// <summary>
        /// Embedding endpoint, or null.
        /// </summary>
        public ModelEndpointConfig? Embedding { get; set; } = null;

        /// <summary>
        /// System prompt: compiled instructions plus any caller context.
        /// </summary>
        public string SystemPrompt { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="turn">Claimed conversation.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AgentTurnPlan(AgentTurnOptions options, ActiveTurn turn)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Turn = turn ?? throw new ArgumentNullException(nameof(turn));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Release the conversation.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Release resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (disposing) Turn.Dispose();
        }

        #endregion
    }
}
