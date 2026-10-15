namespace Hugo.Connectors.Agents.A2A.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.A2A.Payloads;
    using Voltaic.A2A;

    /// <summary>
    /// <c>a2a_delegate</c>: send a message to the remote A2A agent and wait until its task completes, fails, or needs
    /// input, within the timeout. Returns the agent's final message and artifacts, bounded. <c>contextId</c> continues a
    /// conversation; <c>taskId</c> answers a task that is waiting for input. An input-required task is a normal result
    /// that says what the remote agent needs. Always Write, because the remote agent may act on the request.
    /// Thread-safe.
    /// </summary>
    public class A2ADelegateTool : A2AToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public static readonly string ToolName = "a2a_delegate";

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"message\"],\"properties\":{"
            + "\"message\":{\"type\":\"string\",\"minLength\":1,\"description\":\"What you want the remote agent to do or answer, in plain language. Include every detail it needs; it cannot see this conversation.\"},"
            + "\"contextId\":{\"type\":\"string\",\"description\":\"Continue an earlier conversation with this agent (contextId from a previous result).\"},"
            + "\"taskId\":{\"type\":\"string\",\"description\":\"Answer a task that is input-required (taskId from that result). Send contextId with it.\"},"
            + "\"data\":{\"type\":\"object\",\"description\":\"Optional structured data sent as a data part next to the message.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public A2ADelegateTool(A2AAgentConnectorOptions options) : base(BuildDescriptor(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string Summarize(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string message = arguments == null ? String.Empty : TextClip.Clip(TextClip.OneLine(arguments.GetString("message")), 200);
            string target = Target(context);
            string continuing = arguments != null && arguments.Has("taskId") ? " (answering task " + arguments.GetString("taskId") + ")" : String.Empty;
            return "Delegate to remote agent " + target + continuing + ": \"" + message + "\"";
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, A2AAgentResourceOptions resource, A2ARemoteSession session, ToolArguments arguments, TimeSpan limit, CancellationToken callerToken, CancellationToken token)
        {
            string text = arguments.GetRequiredString("message");
            string? contextId = Optional(arguments.GetString("contextId"));
            string? taskId = Optional(arguments.GetString("taskId"));

            Message message = new Message
            {
                Role = Role.User,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = contextId,
                TaskId = taskId,
                Parts = new List<Part> { Part.FromText(text) }
            };
            if (arguments.Has("data")) message.Parts.Add(Part.FromData(AgentsJson.ParseObject(arguments.GetRawJson("data"), "data")));
            Dictionary<string, object?>? metadata = resource.SkillId == null ? null : new Dictionary<string, object?>(StringComparer.Ordinal) { { "skillId", resource.SkillId } };
            message.Metadata = metadata;

            SendMessageRequest request = new SendMessageRequest
            {
                Message = message,
                Metadata = metadata == null ? null : new Dictionary<string, object?>(metadata, StringComparer.Ordinal),
                Configuration = new SendMessageConfiguration { ReturnImmediately = false }
            };

            string agent = TextClip.OneLine(session.Card.Name);
            AgentTask? task = null;
            Message? direct = null;
            string? knownTaskId = taskId;
            string? knownContextId = contextId;
            bool streamed = resource.Streaming && session.SupportsStreaming;
            try
            {
                if (streamed)
                {
                    A2AStreamAccumulator acc = new A2AStreamAccumulator();
                    await foreach (StreamResponse item in session.StreamAsync(request, token).WithCancellation(token).ConfigureAwait(false))
                    {
                        acc.Apply(item);
                        knownTaskId = acc.TaskId ?? knownTaskId;
                        if (!String.IsNullOrEmpty(acc.Task?.ContextId)) knownContextId = acc.Task.ContextId;
                        if (acc.IsDone) break;
                    }
                    direct = acc.DirectMessage;
                    task = acc.Task;
                    if (direct == null && task != null && !acc.IsDone && !String.IsNullOrEmpty(task.Id))
                        task = await PollAsync(session, task, token).ConfigureAwait(false);
                }
                else
                {
                    SendMessageResponse response = await session.SendAsync(request, token).ConfigureAwait(false);
                    direct = response.Message;
                    task = response.Task;
                    if (task != null)
                    {
                        knownTaskId = String.IsNullOrEmpty(task.Id) ? knownTaskId : task.Id;
                        knownContextId = String.IsNullOrEmpty(task.ContextId) ? knownContextId : task.ContextId;
                        task = await PollAsync(session, task, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
            {
                A2ATaskPayload pending = new A2ATaskPayload
                {
                    Agent = agent,
                    Status = "working",
                    TaskId = knownTaskId,
                    ContextId = knownContextId,
                    Next = knownTaskId == null ? null : "Check later with a2a_get_task {\"taskId\": \"" + knownTaskId + "\"}."
                };
                string where = knownTaskId == null ? "." : " and is still running as task " + knownTaskId + ". Check it later with a2a_get_task.";
                ToolResult timeout = ToolResult.Error(ConnectorErrorCodes.Timeout, "The remote agent did not finish within " + limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds" + where);
                timeout.StructuredJson = AgentsJson.Write(pending);
                return timeout;
            }

            if (direct != null)
            {
                A2ATaskPayload reply = A2ATaskRenderer.FromMessage(direct, agent, Options.MaxTextChars);
                reply.ContextId ??= knownContextId;
                if (reply.ContextId != null) reply.Next = "To follow up, call a2a_delegate with contextId \"" + reply.ContextId + "\".";
                return TaskResult(context, reply);
            }
            if (task == null)
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The remote agent answered without a task or a message.");

            A2ATaskPayload payload = A2ATaskRenderer.FromTask(task, agent, Options.MaxTextChars, context.Limits.MaxRows);
            TaskState state = task.Status?.State ?? TaskState.Unspecified;
            if (state == TaskState.InputRequired)
            {
                payload.Next = "The remote agent is waiting. Answer its request (ask the user if you do not know the answer), then call a2a_delegate with taskId \""
                    + payload.TaskId + "\"" + (payload.ContextId == null ? String.Empty : " and contextId \"" + payload.ContextId + "\"") + " and your answer as message.";
                return TaskResult(context, payload);
            }
            if (state == TaskState.Completed)
            {
                if (payload.ContextId != null) payload.Next = "To follow up, call a2a_delegate with contextId \"" + payload.ContextId + "\".";
                return TaskResult(context, payload);
            }

            ToolResult result = TaskResult(context, payload);
            ToolResult error;
            if (state == TaskState.AuthRequired)
                error = ToolResult.Error(ConnectorErrorCodes.AuthFailed, "The remote agent requires additional authentication that Hugo cannot provide.\n" + result.Text);
            else if (A2ATaskRenderer.IsTerminal(state))
                error = ToolResult.Error(ConnectorErrorCodes.UpstreamError, "The remote agent's task ended as " + payload.Status + ".\n" + result.Text);
            else
                error = ToolResult.Error(ConnectorErrorCodes.UpstreamError, "The remote agent stopped answering while its task was " + payload.Status + ".\n" + result.Text);
            error.StructuredJson = result.StructuredJson;
            error.Truncated = result.Truncated;
            error.OriginalBytes = result.OriginalBytes;
            return error;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = ToolName,
                Title = "Delegate to remote agent",
                Description =
                    "Send a task to the remote A2A agent and wait for its answer: the final message and any artifacts, or, when the agent needs more information, "
                    + "a result with status input-required that says what it needs plus the taskId and contextId to answer with. Use contextId to continue a conversation. "
                    + "Delegate only work that fits the agent's skills (see a2a_get_card). The agent cannot see this conversation, so include every detail it needs, "
                    + "and never include secrets. Its answers are its own claims, not verified facts.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Write,
                OutputDescription = "Text with the status, the reply fenced as untrusted content, and artifacts, plus structured JSON {agent, status, terminal, needsInput, taskId, contextId, message, artifacts[{artifactId, name, description, text, files}], artifactCount, next}. Failed, rejected, and canceled tasks are tool errors with code upstream_error; auth-required is auth_failed; a timeout is timeout with the taskId to check later."
            };
        }

        private async Task<AgentTask> PollAsync(A2ARemoteSession session, AgentTask task, CancellationToken token)
        {
            AgentTask current = task;
            if (String.IsNullOrEmpty(current.Id)) return current;
            while (!A2ATaskRenderer.IsTerminal(current.Status?.State ?? TaskState.Unspecified) && !A2ATaskRenderer.IsInterrupted(current.Status?.State ?? TaskState.Unspecified))
            {
                await Task.Delay(Options.PollInterval, token).ConfigureAwait(false);
                current = await session.GetTaskAsync(current.Id, null, token).ConfigureAwait(false);
            }
            return current;
        }

        private static string? Optional(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
