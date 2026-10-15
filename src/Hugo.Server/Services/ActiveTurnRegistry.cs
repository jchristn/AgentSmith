namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// Turns running on this server, one per conversation (P6.20). A second turn on a busy conversation is refused before
    /// any response is written, so an SSE request can still answer 409. The cancel route and A2A CancelTask cancel through
    /// here (P6.13). Thread-safe.
    /// </summary>
    public class ActiveTurnRegistry
    {
        #region Public-Members

        /// <summary>
        /// Number of running turns.
        /// </summary>
        public int Count
        {
            get
            {
                return _Turns.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, ActiveTurn> _Turns = new ConcurrentDictionary<string, ActiveTurn>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a turn on a conversation.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <returns>The turn (dispose it when the turn ends), or null when another turn is running.</returns>
        /// <exception cref="ArgumentNullException">Thrown when conversationId is null or empty.</exception>
        public ActiveTurn? TryBegin(string conversationId)
        {
            if (String.IsNullOrEmpty(conversationId)) throw new ArgumentNullException(nameof(conversationId));
            ActiveTurn turn = new ActiveTurn(conversationId, Release);
            if (_Turns.TryAdd(conversationId, turn)) return turn;
            turn.Dispose();
            return null;
        }

        /// <summary>
        /// Whether a turn is running on a conversation.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <returns>True when running.</returns>
        public bool IsActive(string conversationId)
        {
            return !String.IsNullOrEmpty(conversationId) && _Turns.ContainsKey(conversationId);
        }

        /// <summary>
        /// Cancel the turn running on a conversation.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <returns>True when a running turn was signalled.</returns>
        public bool Cancel(string conversationId)
        {
            if (String.IsNullOrEmpty(conversationId)) return false;
            if (!_Turns.TryGetValue(conversationId, out ActiveTurn? turn)) return false;
            turn.Cancel();
            return true;
        }

        /// <summary>
        /// Cancel every running turn (shutdown).
        /// </summary>
        public void CancelAll()
        {
            foreach (KeyValuePair<string, ActiveTurn> entry in _Turns) entry.Value.Cancel();
        }

        #endregion

        #region Private-Methods

        private void Release(ActiveTurn turn)
        {
            _Turns.TryRemove(new KeyValuePair<string, ActiveTurn>(turn.ConversationId, turn));
        }

        #endregion
    }
}
