namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Convenience base for tools. The default <see cref="Classify"/> returns the descriptor's base effect with a summary
    /// built by <see cref="Summarize"/>. Thread-safe when derived classes keep no per-call state in fields.
    /// </summary>
    public abstract class ConnectorToolBase : IConnectorTool
    {
        /// <inheritdoc />
        public ToolDescriptor Descriptor { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Descriptor.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected ConnectorToolBase(ToolDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        /// <inheritdoc />
        public virtual EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            return new EffectClassification(Descriptor.BaseEffect, "tool " + Descriptor.Name + " is " + Descriptor.BaseEffect, Summarize(context, arguments));
        }

        /// <inheritdoc />
        public abstract Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default);

        /// <summary>
        /// Build the approval card summary. Default: <c>{title} on {alias}</c>.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Summary.</returns>
        protected virtual string Summarize(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string title = String.IsNullOrEmpty(Descriptor.Title) ? Descriptor.Name : Descriptor.Title;
            string target = context == null ? String.Empty : (String.IsNullOrEmpty(context.BindingAlias) ? context.Resource.Name : context.BindingAlias);
            return String.IsNullOrEmpty(target) ? title : title + " on " + target;
        }
    }
}
