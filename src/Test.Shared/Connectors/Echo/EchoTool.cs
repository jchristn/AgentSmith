namespace Test.Shared.Connectors.Echo
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One tool of the test echo connector (P3.20). Echo tools return their text argument (repeated, optionally with the
    /// bound token secret so scrubbing can be tested, optionally after a delay so timeouts and cancellation can be
    /// tested). The classify tool decides its effect from the command argument: read..., write..., delete..., reject...,
    /// anything else is Destructive.
    /// </summary>
    public class EchoTool : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Behavior.
        /// </summary>
        public EchoToolKindEnum Kind { get; }

        #endregion

        #region Private-Members

        private static readonly string _EchoSchema = "{\"type\":\"object\",\"properties\":{"
            + "\"text\":{\"type\":\"string\",\"description\":\"Text to echo.\"},"
            + "\"repeat\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100000},"
            + "\"delayMs\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":600000},"
            + "\"revealSecret\":{\"type\":\"boolean\"}"
            + "},\"additionalProperties\":false}";
        private static readonly string _ClassifySchema = "{\"type\":\"object\",\"properties\":{"
            + "\"command\":{\"type\":\"string\",\"description\":\"Command; its first word decides the effect.\"},"
            + "\"text\":{\"type\":\"string\"}"
            + "},\"required\":[\"command\"],\"additionalProperties\":false}";
        private static readonly string _KnowledgeSchema = "{\"type\":\"object\",\"properties\":{"
            + "\"scope\":{\"type\":\"string\"},\"key\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"}"
            + "},\"required\":[\"scope\",\"content\"],\"additionalProperties\":false}";
        private static readonly string _ModelSchema = "{\"type\":\"object\",\"properties\":{"
            + "\"prompt\":{\"type\":\"string\"},\"calls\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000}"
            + "},\"required\":[\"prompt\"],\"additionalProperties\":false}";
        private static readonly string _LearnSchema = "{\"type\":\"object\",\"properties\":{"
            + "\"fingerprint\":{\"type\":\"string\"}"
            + "},\"required\":[\"fingerprint\"],\"additionalProperties\":false}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="effect">Base effect.</param>
        /// <param name="kind">Behavior.</param>
        public EchoTool(string name, EffectEnum effect, EchoToolKindEnum kind) : base(Describe(name, effect, kind))
        {
            Kind = kind;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            if (Kind != EchoToolKindEnum.Classify) return base.Classify(context, arguments);
            string command = (arguments.GetString("command") ?? String.Empty).Trim();
            string summary = "Run '" + command + "' on " + (context?.BindingAlias ?? "echo");
            if (command.StartsWith("read", StringComparison.OrdinalIgnoreCase)) return new EffectClassification(EffectEnum.Read, "command starts with read", summary);
            if (command.StartsWith("write", StringComparison.OrdinalIgnoreCase)) return new EffectClassification(EffectEnum.Write, "command starts with write", summary);
            if (command.StartsWith("delete", StringComparison.OrdinalIgnoreCase)) return new EffectClassification(EffectEnum.Destructive, "command starts with delete", summary);
            if (command.StartsWith("reject", StringComparison.OrdinalIgnoreCase)) return EffectClassification.Reject("command starts with reject");
            return new EffectClassification(EffectEnum.Destructive, "unknown command (fail closed)", summary);
        }

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            switch (Kind)
            {
                case EchoToolKindEnum.Knowledge:
                    ResourceKnowledgeEntry entry = new ResourceKnowledgeEntry();
                    entry.Scope = arguments.GetRequiredString("scope");
                    entry.Key = arguments.GetString("key") ?? String.Empty;
                    entry.Content = arguments.GetRequiredString("content");
                    entry.Source = KnowledgeSourceEnum.Agent;
                    ResourceKnowledgeEntry stored = await context.Resource.Knowledge.PutAsync(entry, token).ConfigureAwait(false);
                    return ToolResult.FromText("stored " + stored.Scope + "/" + stored.Key + " version " + stored.Version);
                case EchoToolKindEnum.Model:
                    if (context.Model == null) return ToolResult.Error(ConnectorErrorCodes.NotSupported, "No model.");
                    int calls = arguments.GetInt("calls", 1, 1, 1000);
                    StringBuilder answers = new StringBuilder();
                    for (int i = 0; i < calls; i++)
                    {
                        ConnectorModelRequest request = new ConnectorModelRequest();
                        request.SystemPrompt = "You are a test.";
                        request.UserPrompt = arguments.GetRequiredString("prompt");
                        request.Purpose = "echo-test";
                        ConnectorModelResponse response = await context.Model.CompleteAsync(request, token).ConfigureAwait(false);
                        answers.AppendLine(response.Text);
                    }
                    return ToolResult.FromText(answers.ToString().TrimEnd());
                case EchoToolKindEnum.Learn:
                    context.Resource.LearnSetting?.Invoke("hostKeyFingerprint", arguments.GetRequiredString("fingerprint"));
                    return ToolResult.FromText("learned");
                default:
                    int delay = arguments.GetInt("delayMs", 0, 0, 600000);
                    if (delay > 0) await Task.Delay(delay, token).ConfigureAwait(false);
                    string text = arguments.GetString("text") ?? arguments.GetString("command") ?? String.Empty;
                    int repeat = arguments.GetInt("repeat", 1, 1, 100000);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < repeat; i++) sb.Append(text);
                    if (arguments.GetBool("revealSecret", false)) sb.Append(" token=").Append(context.Resource.Secrets.Get("token", "token") ?? "none");
                    sb.Append(" greeting=").Append(context.Resource.Settings.GetString("greeting") ?? String.Empty);
                    return ToolResult.FromText(sb.ToString());
            }
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor Describe(string name, EffectEnum effect, EchoToolKindEnum kind)
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = name;
            d.Title = name;
            d.Description = "Test tool " + name + ".";
            d.BaseEffect = effect;
            d.ClassifiedPerCall = kind == EchoToolKindEnum.Classify;
            d.UpdatesKnowledge = kind == EchoToolKindEnum.Knowledge;
            d.RequiresModel = kind == EchoToolKindEnum.Model;
            switch (kind)
            {
                case EchoToolKindEnum.Classify:
                    d.InputSchemaJson = _ClassifySchema;
                    break;
                case EchoToolKindEnum.Knowledge:
                    d.InputSchemaJson = _KnowledgeSchema;
                    break;
                case EchoToolKindEnum.Model:
                    d.InputSchemaJson = _ModelSchema;
                    break;
                case EchoToolKindEnum.Learn:
                    d.InputSchemaJson = _LearnSchema;
                    break;
                default:
                    d.InputSchemaJson = _EchoSchema;
                    break;
            }
            return d;
        }

        #endregion
    }
}
