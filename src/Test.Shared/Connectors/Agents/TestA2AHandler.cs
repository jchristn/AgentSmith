namespace Test.Shared.Connectors.Agents
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.A2A;

    /// <summary>
    /// Scripted A2A agent behavior for the <c>a2a-agent</c> suites. The first word-level cue in the message decides:
    /// <c>need input</c> asks for input, a continuation completes with the answer, <c>report</c> adds an artifact,
    /// <c>fail</c> fails, <c>slow</c> waits four seconds, <c>direct</c> replies with a message instead of a task, and
    /// anything else is echoed. Thread-safe.
    /// </summary>
    public class TestA2AHandler : IA2AAgentHandler
    {
        #region Public-Members

        /// <summary>
        /// skillId metadata values received.
        /// </summary>
        public ConcurrentQueue<string> SkillIds { get; } = new ConcurrentQueue<string>();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(A2ARequestContext context, A2AAgentEventQueue eventQueue, CancellationToken token)
        {
            string text = String.Join(" ", (context.Message.Parts ?? new List<Part>()).Where(p => p.Text != null).Select(p => p.Text));
            string skill = String.Empty;
            if (context.Message.Metadata != null && context.Message.Metadata.TryGetValue("skillId", out object? value) && value != null)
            {
                skill = value.ToString() ?? String.Empty;
                SkillIds.Enqueue(skill);
            }

            A2ATaskUpdater updater = new A2ATaskUpdater(eventQueue, context.TaskId, context.ContextId);
            if (context.IsContinuation)
            {
                await updater.CompleteAsync(Reply(context, "Thanks, deploying to " + text + "."), token).ConfigureAwait(false);
                return;
            }
            if (text.Contains("direct", StringComparison.OrdinalIgnoreCase))
            {
                await updater.ReturnMessageAsync(Reply(context, "direct reply"), token).ConfigureAwait(false);
                return;
            }

            await updater.SubmitAsync(token: token).ConfigureAwait(false);
            await updater.StartAsync(token: token).ConfigureAwait(false);
            if (text.Contains("need input", StringComparison.OrdinalIgnoreCase))
            {
                await updater.UpdateStatusAsync(TaskState.InputRequired, Reply(context, "Which environment: staging or production?"), token).ConfigureAwait(false);
                return;
            }
            if (text.Contains("fail", StringComparison.OrdinalIgnoreCase))
            {
                await updater.FailAsync(Reply(context, "Cannot do that: the backend is down."), token).ConfigureAwait(false);
                return;
            }
            if (text.Contains("slow", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(4000, token).ConfigureAwait(false);
            }
            if (text.Contains("report", StringComparison.OrdinalIgnoreCase))
            {
                Artifact artifact = new Artifact
                {
                    ArtifactId = "a1",
                    Name = "report",
                    Description = "Weekly report",
                    Parts = new List<Part> { Part.FromText("artifact body line"), Part.FromData(new Dictionary<string, object?> { { "count", 3 } }) }
                };
                await updater.AddArtifactAsync(artifact, token: token).ConfigureAwait(false);
            }
            await updater.CompleteAsync(Reply(context, "Echo: " + text + " skill=" + skill + " ctx=" + context.ContextId), token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static Message Reply(A2ARequestContext context, string text)
        {
            return new Message
            {
                Role = Role.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = context.ContextId,
                TaskId = context.TaskId,
                Parts = new List<Part> { Part.FromText(text) }
            };
        }

        #endregion
    }
}
