namespace Hugo.Connectors.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One operation a connector exposes to agents. Implementations must be thread-safe: one instance serves every call.
    /// </summary>
    public interface IConnectorTool
    {
        /// <summary>
        /// Tool descriptor.
        /// </summary>
        ToolDescriptor Descriptor { get; }

        /// <summary>
        /// Classify a specific call. Must be fast, side-effect free, and must fail closed: input that cannot be
        /// understood classifies as <see cref="EffectEnum.Destructive"/>.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Validated arguments.</param>
        /// <returns>Classification.</returns>
        EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments);

        /// <summary>
        /// Execute the call. Must honor the token, the limits, and the timeout on the context. Expected failures
        /// (unreachable host, missing object, upstream error) return <see cref="ToolResult.Error"/> or throw
        /// <see cref="ConnectorException"/>.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Validated arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default);
    }
}
