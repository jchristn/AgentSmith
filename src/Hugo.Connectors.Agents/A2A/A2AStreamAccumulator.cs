namespace Hugo.Connectors.Agents.A2A
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Voltaic.A2A;

    /// <summary>
    /// Folds the events of a streaming A2A delegation (task snapshots, status updates, artifact updates, or a direct
    /// message) into one current view of the task. Not thread-safe; one instance per delegation.
    /// </summary>
    public class A2AStreamAccumulator
    {
        #region Public-Members

        /// <summary>
        /// The task as known so far, or null before the first task event.
        /// </summary>
        public AgentTask? Task { get; private set; } = null;

        /// <summary>
        /// A direct message reply (no task), or null.
        /// </summary>
        public Message? DirectMessage { get; private set; } = null;

        /// <summary>
        /// Task identifier seen so far, or null.
        /// </summary>
        public string? TaskId
        {
            get
            {
                return String.IsNullOrEmpty(Task?.Id) ? null : Task.Id;
            }
        }

        /// <summary>
        /// Whether the delegation has an answer: a direct message, a final state, or a state waiting for the caller.
        /// </summary>
        public bool IsDone
        {
            get
            {
                if (DirectMessage != null) return true;
                if (Task == null) return false;
                TaskState state = Task.Status?.State ?? TaskState.Unspecified;
                return A2ATaskRenderer.IsTerminal(state) || A2ATaskRenderer.IsInterrupted(state);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply one event.
        /// </summary>
        /// <param name="item">Event. Null is ignored.</param>
        public void Apply(StreamResponse? item)
        {
            if (item == null) return;
            if (item.Message != null)
            {
                DirectMessage = item.Message;
                return;
            }
            if (item.Task != null)
            {
                Task = item.Task;
                return;
            }
            if (item.StatusUpdate != null)
            {
                AgentTask task = EnsureTask(item.StatusUpdate.TaskId, item.StatusUpdate.ContextId);
                if (item.StatusUpdate.Status != null) task.Status = item.StatusUpdate.Status;
                return;
            }
            if (item.ArtifactUpdate != null)
            {
                AgentTask task = EnsureTask(item.ArtifactUpdate.TaskId, item.ArtifactUpdate.ContextId);
                Artifact incoming = item.ArtifactUpdate.Artifact ?? new Artifact();
                task.Artifacts ??= new List<Artifact>();
                Artifact? existing = task.Artifacts.FirstOrDefault(a => String.Equals(a.ArtifactId, incoming.ArtifactId, StringComparison.Ordinal));
                if (existing != null && item.ArtifactUpdate.Append)
                {
                    existing.Parts ??= new List<Part>();
                    existing.Parts.AddRange(incoming.Parts ?? new List<Part>());
                }
                else if (existing != null)
                {
                    task.Artifacts[task.Artifacts.IndexOf(existing)] = incoming;
                }
                else
                {
                    task.Artifacts.Add(incoming);
                }
            }
        }

        #endregion

        #region Private-Methods

        private AgentTask EnsureTask(string? taskId, string? contextId)
        {
            if (Task == null) Task = new AgentTask { Id = taskId ?? String.Empty, ContextId = contextId ?? String.Empty };
            if (String.IsNullOrEmpty(Task.Id) && !String.IsNullOrEmpty(taskId)) Task.Id = taskId;
            if (String.IsNullOrEmpty(Task.ContextId) && !String.IsNullOrEmpty(contextId)) Task.ContextId = contextId;
            return Task;
        }

        #endregion
    }
}
