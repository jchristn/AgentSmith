namespace Hugo.Connectors.Agents.A2A.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.A2A.Payloads;
    using Voltaic.A2A;

    /// <summary>
    /// <c>a2a_get_task</c>: read the current state of a task on the remote A2A agent (for example one that timed out in
    /// <c>a2a_delegate</c>), with its latest message and artifacts. Always Read. Thread-safe.
    /// </summary>
    public class A2AGetTaskTool : A2AToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public static readonly string ToolName = "a2a_get_task";

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"taskId\"],\"properties\":{"
            + "\"taskId\":{\"type\":\"string\",\"minLength\":1,\"description\":\"Task identifier from a2a_delegate.\"},"
            + "\"historyLength\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":50,\"description\":\"Most history messages the agent returns, used to find its latest reply. Default: the agent's default.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public A2AGetTaskTool(A2AAgentConnectorOptions options) : base(BuildDescriptor(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, A2AAgentResourceOptions resource, A2ARemoteSession session, ToolArguments arguments, TimeSpan limit, CancellationToken callerToken, CancellationToken token)
        {
            string taskId = arguments.GetRequiredString("taskId").Trim();
            int? history = arguments.Has("historyLength") ? arguments.GetInt("historyLength", 0, 0, 50) : null;
            AgentTask task = await session.GetTaskAsync(taskId, history, token).ConfigureAwait(false);
            A2ATaskPayload payload = A2ATaskRenderer.FromTask(task, TextClip.OneLine(session.Card.Name), Options.MaxTextChars, context.Limits.MaxRows);
            if (payload.NeedsInput)
                payload.Next = "The remote agent is waiting for input. Answer with a2a_delegate using taskId \"" + payload.TaskId + "\"" + (payload.ContextId == null ? String.Empty : " and contextId \"" + payload.ContextId + "\"") + ".";
            else if (!payload.Terminal && payload.Status != "auth-required")
                payload.Next = "The task is still " + payload.Status + "; check again later.";
            return TaskResult(context, payload);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = ToolName,
                Title = "Get remote agent task",
                Description =
                    "Read a task on the remote A2A agent by taskId: its status (working, completed, failed, input-required, ...), its latest message, and its artifacts. "
                    + "Use it after a2a_delegate timed out, or to re-read a result. The content is the remote agent's own claims.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text with the status, the latest reply fenced as untrusted content, and artifacts, plus structured JSON {agent, status, terminal, needsInput, taskId, contextId, message, artifacts[], artifactCount, next}."
            };
        }

        #endregion
    }
}
