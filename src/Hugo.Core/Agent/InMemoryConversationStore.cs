namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory <see cref="IConversationStore"/> for tests and benchmarks. Not durable (D-027 requires the
    /// database store in production). Thread-safe.
    /// </summary>
    public class InMemoryConversationStore : IConversationStore
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private readonly Dictionary<string, List<AgentMessage>> _Active = new Dictionary<string, List<AgentMessage>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AgentMessage>> _All = new Dictionary<string, List<AgentMessage>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AgentCompaction>> _Compactions = new Dictionary<string, List<AgentCompaction>>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<AgentMessage>> LoadHistoryAsync(string conversationId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(conversationId);
            token.ThrowIfCancellationRequested();
            lock (_Lock)
            {
                if (!_Active.TryGetValue(conversationId, out List<AgentMessage>? list)) return Task.FromResult(new List<AgentMessage>());
                return Task.FromResult(list.ToList());
            }
        }

        /// <inheritdoc />
        public Task AppendMessagesAsync(string conversationId, List<AgentMessage> messages, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(conversationId);
            ArgumentNullException.ThrowIfNull(messages);
            token.ThrowIfCancellationRequested();
            lock (_Lock)
            {
                Get(_Active, conversationId).AddRange(messages);
                Get(_All, conversationId).AddRange(messages);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SaveCompactionAsync(string conversationId, AgentCompaction compaction, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(conversationId);
            ArgumentNullException.ThrowIfNull(compaction);
            token.ThrowIfCancellationRequested();
            lock (_Lock)
            {
                HashSet<string> covered = new HashSet<string>(compaction.CoveredMessageIds, StringComparer.Ordinal);
                List<AgentMessage> active = Get(_Active, conversationId);
                List<AgentMessage> kept = active.Where(m => !covered.Contains(m.Id)).ToList();
                active.Clear();
                active.Add(compaction.Summary);
                active.AddRange(kept);
                Get(_All, conversationId).Add(compaction.Summary);
                if (!_Compactions.TryGetValue(conversationId, out List<AgentCompaction>? list))
                {
                    list = new List<AgentCompaction>();
                    _Compactions[conversationId] = list;
                }
                list.Add(compaction);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Seed a conversation's history (tests).
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <param name="messages">Messages.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public void Seed(string conversationId, IEnumerable<AgentMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(conversationId);
            ArgumentNullException.ThrowIfNull(messages);
            lock (_Lock)
            {
                List<AgentMessage> list = messages.ToList();
                Get(_Active, conversationId).AddRange(list);
                Get(_All, conversationId).AddRange(list);
            }
        }

        /// <summary>
        /// Every message ever stored for a conversation, including compacted ones and summaries, in write order.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <returns>Messages. Never null.</returns>
        public List<AgentMessage> AllMessages(string conversationId)
        {
            lock (_Lock)
            {
                if (conversationId == null || !_All.TryGetValue(conversationId, out List<AgentMessage>? list)) return new List<AgentMessage>();
                return list.ToList();
            }
        }

        /// <summary>
        /// Compactions saved for a conversation, oldest first.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <returns>Compactions. Never null.</returns>
        public List<AgentCompaction> Compactions(string conversationId)
        {
            lock (_Lock)
            {
                if (conversationId == null || !_Compactions.TryGetValue(conversationId, out List<AgentCompaction>? list)) return new List<AgentCompaction>();
                return list.ToList();
            }
        }

        #endregion

        #region Private-Methods

        private static List<AgentMessage> Get(Dictionary<string, List<AgentMessage>> map, string conversationId)
        {
            if (!map.TryGetValue(conversationId, out List<AgentMessage>? list))
            {
                list = new List<AgentMessage>();
                map[conversationId] = list;
            }
            return list;
        }

        #endregion
    }
}
