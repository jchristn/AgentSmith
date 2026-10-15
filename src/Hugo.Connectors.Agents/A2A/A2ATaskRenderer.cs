namespace Hugo.Connectors.Agents.A2A
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Hugo.Connectors.Agents.A2A.Payloads;
    using Voltaic.A2A;

    /// <summary>
    /// Turns remote A2A tasks and messages into bounded structured payloads and text for the calling model. Remote text
    /// is clipped, fence tags inside it are neutralized, and the agent's reply is fenced as untrusted third-party content.
    /// Binary parts are summarized, never included. Thread-safe.
    /// </summary>
    public static class A2ATaskRenderer
    {
        #region Public-Methods

        /// <summary>
        /// Wire-style name of a task state (for example <c>input-required</c>).
        /// </summary>
        /// <param name="state">State.</param>
        /// <returns>Name.</returns>
        public static string StateName(TaskState state)
        {
            switch (state)
            {
                case TaskState.Submitted: return "submitted";
                case TaskState.Working: return "working";
                case TaskState.Completed: return "completed";
                case TaskState.Failed: return "failed";
                case TaskState.Canceled: return "canceled";
                case TaskState.InputRequired: return "input-required";
                case TaskState.Rejected: return "rejected";
                case TaskState.AuthRequired: return "auth-required";
                default: return "unknown";
            }
        }

        /// <summary>
        /// Whether a state is final (completed, failed, canceled, rejected).
        /// </summary>
        /// <param name="state">State.</param>
        /// <returns>True when final.</returns>
        public static bool IsTerminal(TaskState state)
        {
            return state == TaskState.Completed || state == TaskState.Failed || state == TaskState.Canceled || state == TaskState.Rejected;
        }

        /// <summary>
        /// Whether a state waits for the caller (input-required, auth-required).
        /// </summary>
        /// <param name="state">State.</param>
        /// <returns>True when interrupted.</returns>
        public static bool IsInterrupted(TaskState state)
        {
            return state == TaskState.InputRequired || state == TaskState.AuthRequired;
        }

        /// <summary>
        /// Text of a message: text parts as-is, data parts as compact JSON, file parts summarized.
        /// </summary>
        /// <param name="message">Message, or null.</param>
        /// <param name="maxChars">Most characters kept.</param>
        /// <returns>Text, or null when the message is null or has no parts.</returns>
        public static string? MessageText(Message? message, int maxChars)
        {
            if (message == null || message.Parts == null || !message.Parts.Any()) return null;
            List<string> files = new List<string>();
            string text = PartsText(message.Parts, files);
            if (files.Any()) text = text + (text.Length > 0 ? "\n" : String.Empty) + String.Join("\n", files.Select(f => "[" + f + "]"));
            return TextClip.Clip(UntrustedContent.Neutralize(text), maxChars);
        }

        /// <summary>
        /// Payload for a task.
        /// </summary>
        /// <param name="task">Task.</param>
        /// <param name="agent">Agent name.</param>
        /// <param name="maxChars">Most characters per message or artifact.</param>
        /// <param name="maxArtifacts">Most artifacts included.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">Thrown when task is null.</exception>
        public static A2ATaskPayload FromTask(AgentTask task, string agent, int maxChars, int maxArtifacts)
        {
            ArgumentNullException.ThrowIfNull(task);
            TaskState state = task.Status?.State ?? TaskState.Unspecified;
            A2ATaskPayload p = new A2ATaskPayload
            {
                Agent = agent ?? String.Empty,
                Status = StateName(state),
                Terminal = IsTerminal(state),
                NeedsInput = state == TaskState.InputRequired,
                TaskId = String.IsNullOrEmpty(task.Id) ? null : task.Id,
                ContextId = String.IsNullOrEmpty(task.ContextId) ? null : task.ContextId
            };

            Message? reply = task.Status?.Message;
            if (reply == null && task.History != null) reply = task.History.LastOrDefault(m => m != null && m.Role == Role.Agent);
            p.Message = MessageText(reply, maxChars);

            List<Artifact> artifacts = (task.Artifacts ?? new List<Artifact>()).Where(a => a != null).ToList();
            p.ArtifactCount = artifacts.Count;
            foreach (Artifact a in artifacts.Take(Math.Max(0, maxArtifacts)))
            {
                List<string> files = new List<string>();
                string text = PartsText(a.Parts ?? new List<Part>(), files);
                p.Artifacts.Add(new A2AArtifactPayload
                {
                    ArtifactId = a.ArtifactId ?? String.Empty,
                    Name = a.Name,
                    Description = a.Description == null ? null : TextClip.Clip(UntrustedContent.Neutralize(a.Description), 1000),
                    Text = TextClip.Clip(UntrustedContent.Neutralize(text), maxChars),
                    Files = files
                });
            }
            return p;
        }

        /// <summary>
        /// Payload for a direct message reply (no task).
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="agent">Agent name.</param>
        /// <param name="maxChars">Most characters kept.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
        public static A2ATaskPayload FromMessage(Message message, string agent, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(message);
            return new A2ATaskPayload
            {
                Agent = agent ?? String.Empty,
                Status = "completed",
                Terminal = true,
                TaskId = String.IsNullOrEmpty(message.TaskId) ? null : message.TaskId,
                ContextId = String.IsNullOrEmpty(message.ContextId) ? null : message.ContextId,
                Message = MessageText(message, maxChars)
            };
        }

        /// <summary>
        /// Text for the calling model.
        /// </summary>
        /// <param name="p">Payload.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when p is null.</exception>
        public static string RenderText(A2ATaskPayload p)
        {
            ArgumentNullException.ThrowIfNull(p);
            StringBuilder sb = new StringBuilder();
            sb.Append("Remote agent: ").Append(TextClip.OneLine(UntrustedContent.Neutralize(p.Agent))).Append('\n');
            sb.Append("Status: ").Append(p.Status).Append('\n');
            if (p.TaskId != null) sb.Append("Task: ").Append(p.TaskId).Append('\n');
            if (p.ContextId != null) sb.Append("Context: ").Append(p.ContextId).Append('\n');

            if (p.NeedsInput)
                sb.Append("The remote agent needs more input before it can continue. Its request:\n");
            else if (p.Message != null)
                sb.Append("Reply:\n");
            if (p.Message != null) sb.Append(UntrustedContent.Fence("reply from remote agent " + p.Agent, p.Message));
            else if (p.NeedsInput) sb.Append("(the agent did not say what it needs)\n");

            if (p.ArtifactCount > 0)
            {
                sb.Append("Artifacts: ").Append(p.ArtifactCount.ToString(CultureInfo.InvariantCulture));
                if (p.Artifacts.Count < p.ArtifactCount) sb.Append(" (first ").Append(p.Artifacts.Count.ToString(CultureInfo.InvariantCulture)).Append(" shown)");
                sb.Append('\n');
                foreach (A2AArtifactPayload a in p.Artifacts)
                {
                    string label = TextClip.OneLine(UntrustedContent.Neutralize(a.Name ?? a.ArtifactId));
                    StringBuilder body = new StringBuilder();
                    if (!String.IsNullOrWhiteSpace(a.Description)) body.Append("Description: ").Append(TextClip.OneLine(a.Description)).Append('\n');
                    body.Append(a.Text);
                    foreach (string f in a.Files) body.Append("\n[").Append(f).Append(']');
                    sb.Append(UntrustedContent.Fence("artifact " + label + " from remote agent " + p.Agent, body.ToString()));
                }
            }
            if (p.Next != null) sb.Append("Next: ").Append(p.Next).Append('\n');
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static string PartsText(IEnumerable<Part> parts, List<string> files)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Part part in parts)
            {
                if (part == null) continue;
                switch (part.ContentCase)
                {
                    case PartContentCase.Text:
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(part.Text);
                        break;
                    case PartContentCase.Data:
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(AgentsJson.CompactObject(part.Data) ?? "null");
                        break;
                    case PartContentCase.Raw:
                        files.Add("file " + (part.Filename ?? "(unnamed)") + ", " + (part.MediaType ?? "unknown type") + ", " + (part.Raw?.Length ?? 0).ToString(CultureInfo.InvariantCulture) + " bytes, not shown");
                        break;
                    case PartContentCase.Url:
                        files.Add("file " + (part.Filename ?? "(unnamed)") + ", " + (part.MediaType ?? "unknown type") + ", at " + part.Url);
                        break;
                    default:
                        break;
                }
            }
            return sb.ToString();
        }

        #endregion
    }
}
