namespace Hugo.Server.Services
{
    using System;
    using System.Threading;

    /// <summary>
    /// A running turn registered in <see cref="ActiveTurnRegistry"/>. Disposing it releases the conversation and its
    /// cancellation source. Thread-safe.
    /// </summary>
    public class ActiveTurn : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; }

        /// <summary>
        /// Token cancelled by <see cref="ActiveTurnRegistry.Cancel"/> (the cancel route, A2A CancelTask).
        /// </summary>
        public CancellationToken Token
        {
            get
            {
                return _Cancel.Token;
            }
        }

        /// <summary>
        /// Time the turn started (UTC).
        /// </summary>
        public DateTime StartedUtc { get; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private readonly CancellationTokenSource _Cancel = new CancellationTokenSource();
        private readonly Action<ActiveTurn> _OnDispose;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="onDispose">Called once when disposed.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ActiveTurn(string conversationId, Action<ActiveTurn> onDispose)
        {
            ConversationId = conversationId ?? throw new ArgumentNullException(nameof(conversationId));
            _OnDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Request cancellation of the turn.
        /// </summary>
        public void Cancel()
        {
            try
            {
                _Cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Release the turn.
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
            if (disposing)
            {
                _OnDispose(this);
                _Cancel.Dispose();
            }
        }

        #endregion
    }
}
