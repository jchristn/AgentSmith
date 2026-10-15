namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Voltaic.A2A;
    using Artifact = Voltaic.A2A.Artifact;
    using TaskStatus = Voltaic.A2A.TaskStatus;

    /// <summary>
    /// Maps Hugo's persisted A2A tasks onto Voltaic's A2A 1.0 models (adapted from Garrison A2AMapper). Thread-safe.
    /// </summary>
    public static class A2AMapper
    {
        #region Public-Methods

        /// <summary>
        /// Text of a message: text parts, then data parts as JSON, then URL parts, separated by blank lines.
        /// </summary>
        /// <param name="message">Message, or null.</param>
        /// <returns>Text (empty when there is none).</returns>
        public static string InputText(Message? message)
        {
            if (message == null) return String.Empty;
            StringBuilder sb = new StringBuilder();
            foreach (Part part in message.Parts)
            {
                string? text = part.Text;
                if (text == null && part.Data != null) text = JsonSerializer.Serialize(part.Data, A2AJson.DefaultOptions);
                if (text == null && part.Url != null) text = part.Url;
                if (String.IsNullOrEmpty(text)) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(text);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Wire state of a persisted state.
        /// </summary>
        /// <param name="state">Persisted state.</param>
        /// <returns>Wire state.</returns>
        public static TaskState State(A2ATaskStateEnum state)
        {
            switch (state)
            {
                case A2ATaskStateEnum.Submitted: return TaskState.Submitted;
                case A2ATaskStateEnum.Working: return TaskState.Working;
                case A2ATaskStateEnum.InputRequired: return TaskState.InputRequired;
                case A2ATaskStateEnum.Completed: return TaskState.Completed;
                case A2ATaskStateEnum.Canceled: return TaskState.Canceled;
                case A2ATaskStateEnum.Rejected: return TaskState.Rejected;
                default: return TaskState.Failed;
            }
        }

        /// <summary>
        /// Persisted state of a wire state, for ListTasks filters.
        /// </summary>
        /// <param name="state">Wire state.</param>
        /// <returns>Persisted state, or null for Unspecified and AuthRequired.</returns>
        public static A2ATaskStateEnum? FromWire(TaskState state)
        {
            switch (state)
            {
                case TaskState.Submitted: return A2ATaskStateEnum.Submitted;
                case TaskState.Working: return A2ATaskStateEnum.Working;
                case TaskState.InputRequired: return A2ATaskStateEnum.InputRequired;
                case TaskState.Completed: return A2ATaskStateEnum.Completed;
                case TaskState.Failed: return A2ATaskStateEnum.Failed;
                case TaskState.Canceled: return A2ATaskStateEnum.Canceled;
                case TaskState.Rejected: return A2ATaskStateEnum.Rejected;
                default: return null;
            }
        }

        /// <summary>
        /// Wire task of a persisted task.
        /// </summary>
        /// <param name="task">Task.</param>
        /// <param name="artifacts">Artifacts to include, or null.</param>
        /// <returns>Wire task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when task is null.</exception>
        public static AgentTask Task(A2ATask task, List<A2ATaskArtifact>? artifacts)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            AgentTask wire = new AgentTask();
            wire.Id = task.Id;
            wire.ContextId = task.ContextId;
            wire.Status = Status(task);
            if (artifacts != null && artifacts.Count > 0) wire.Artifacts = artifacts.Select(Artifact).ToList();
            wire.Metadata = new Dictionary<string, object?>
            {
                { "hugo.conversationId", task.ConversationId },
                { "hugo.agentProfileId", task.AgentProfileId },
                { "hugo.inputTokens", task.InputTokens },
                { "hugo.outputTokens", task.OutputTokens },
                { "hugo.toolCallCount", task.ToolCallCount }
            };
            if (!String.IsNullOrEmpty(task.PendingApprovalId)) wire.Metadata["hugo.pendingApprovalId"] = task.PendingApprovalId;
            return wire;
        }

        /// <summary>
        /// Wire status of a persisted task, with the status message as an agent message.
        /// </summary>
        /// <param name="task">Task.</param>
        /// <returns>Status.</returns>
        /// <exception cref="ArgumentNullException">Thrown when task is null.</exception>
        public static TaskStatus Status(A2ATask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            TaskStatus status = new TaskStatus();
            status.State = State(task.State);
            status.Timestamp = new DateTimeOffset(DateTime.SpecifyKind(task.LastUpdateUtc, DateTimeKind.Utc), TimeSpan.Zero);
            if (!String.IsNullOrEmpty(task.StatusMessage))
            {
                Message message = new Message();
                message.Role = Voltaic.A2A.Role.Agent;
                message.MessageId = task.Id + "-" + task.State.ToString().ToLowerInvariant();
                message.TaskId = task.Id;
                message.ContextId = task.ContextId;
                message.Parts.Add(Part.FromText(task.StatusMessage));
                if (!String.IsNullOrEmpty(task.PendingApprovalId)) message.Metadata = new Dictionary<string, object?> { { "hugo.approvalId", task.PendingApprovalId } };
                status.Message = message;
            }
            return status;
        }

        /// <summary>
        /// Wire artifact of a persisted artifact.
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <returns>Wire artifact.</returns>
        /// <exception cref="ArgumentNullException">Thrown when artifact is null.</exception>
        public static Artifact Artifact(A2ATaskArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            Artifact wire = new Artifact();
            wire.ArtifactId = artifact.ArtifactId;
            wire.Name = artifact.Name;
            Part part = Part.FromText(artifact.Content);
            part.MediaType = artifact.MimeType;
            wire.Parts.Add(part);
            return wire;
        }

        #endregion
    }
}
