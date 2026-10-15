namespace Test.Shared.Connectors.Redis
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A tool that reuses a real tool's name with a different input schema, to prove the registry rejects it.
    /// </summary>
    public class RedisSchemaChangedTool : ConnectorToolBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Tool name to reuse.</param>
        public RedisSchemaChangedTool(string name) : base(new ToolDescriptor
        {
            Name = name,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"other\":{\"type\":\"string\"}}}"
        })
        {
        }

        /// <inheritdoc />
        public override Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            return Task.FromResult(ToolResult.FromText("probe"));
        }
    }
}
