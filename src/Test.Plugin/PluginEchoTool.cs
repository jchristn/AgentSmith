namespace Test.Plugin
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The plugin's one tool: returns "plugin:" plus its text argument.
    /// </summary>
    public class PluginEchoTool : ConnectorToolBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public PluginEchoTool() : base(new ToolDescriptor
        {
            Name = "plugecho_read",
            Title = "Plugin echo",
            Description = "Echo text from a plugin.",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}}}",
            BaseEffect = EffectEnum.Read
        })
        {
        }

        /// <inheritdoc />
        public override Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            return Task.FromResult(ToolResult.FromText("plugin:" + (arguments.GetString("text") ?? String.Empty)));
        }
    }
}
