namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for Kubernetes tools: option parsing, classification with namespace and cluster-scope policy, pooled
    /// sessions, the call deadline, and conversion of failures to error results. Execution re-checks the classification,
    /// so a rejected call never reaches the cluster and a ReadOnly binding never runs a change (defense in depth).
    /// Thread-safe; derived tools keep no per-call state in fields.
    /// </summary>
    public abstract class KubernetesToolBase : ConnectorToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Descriptor.</param>
        protected KubernetesToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public sealed override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            KubernetesOptions options = context == null ? new KubernetesOptions() : KubernetesOptions.FromSettings(context.Resource.Settings);
            ToolArguments args = arguments ?? new ToolArguments("{}");
            try
            {
                return ClassifyCall(context, args, options);
            }
            catch (ConnectorException e)
            {
                return new EffectClassification(EffectEnum.Destructive, "the arguments could not be read (" + e.Message + "), so the call is treated as Destructive", Descriptor.Title + " on " + TargetName(context));
            }
        }

        /// <inheritdoc />
        public sealed override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();
            KubernetesOptions options = KubernetesOptions.FromSettings(context.Resource.Settings);

            EffectClassification classification = Classify(context, arguments);
            if (classification.Rejected) return ToolResult.Error("rejected", classification.Reason);
            if (context.AccessMode == AccessModeEnum.ReadOnly && classification.Effect != EffectEnum.Read)
                return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "This binding is ReadOnly and the call is " + classification.Effect + " (" + classification.Reason + ").");

            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(context.Timeout);
                try
                {
                    return await ExecuteCoreAsync(context, arguments, options, deadline.Token).ConfigureAwait(false);
                }
                catch (ConnectorException e)
                {
                    ToolResult error = ToolResult.Error(e.Code, e.Message);
                    error.RetryAfterSeconds = e.RetryAfterSeconds;
                    return error;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ToolResult.Error(ConnectorErrorCodes.Timeout, Descriptor.Name + " did not finish within " + (int)context.Timeout.TotalSeconds + " seconds.");
                }
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Classify a call.
        /// </summary>
        /// <param name="context">Context, or null in offline previews.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Resource options.</param>
        /// <returns>Classification.</returns>
        protected abstract EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options);

        /// <summary>
        /// Execute the tool.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Resource options.</param>
        /// <param name="token">Cancellation token (cancelled at the call deadline).</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token);

        /// <summary>
        /// Classify a call on one kind in one namespace: Rejected when the namespace or the cluster-scoped policy forbids
        /// it, otherwise the given effect.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="kind">Kind or resource name, or null when the tool's kind is fixed.</param>
        /// <param name="apiVersion">apiVersion, or null.</param>
        /// <param name="requestedNamespace">Namespace argument, or null.</param>
        /// <param name="effect">Effect when allowed.</param>
        /// <param name="reason">Reason when allowed.</param>
        /// <param name="summary">Approval summary.</param>
        /// <returns>Classification.</returns>
        protected static EffectClassification Scoped(KubernetesOptions options, string? kind, string? apiVersion, string? requestedNamespace, EffectEnum effect, string reason, string summary)
        {
            bool write = effect != EffectEnum.Read;
            if (!String.IsNullOrEmpty(kind) && KubernetesKnownKinds.IsClusterScoped(kind, apiVersion))
            {
                KubernetesResourceType? known = KubernetesKnownKinds.Find(kind, apiVersion);
                string? clusterReason = KubernetesNamespacePolicy.ExplainClusterScoped(options, known?.Kind ?? kind, write);
                if (clusterReason != null) return Rejected(clusterReason, summary);
                return new EffectClassification(effect, reason, summary);
            }
            if (!String.IsNullOrWhiteSpace(requestedNamespace))
            {
                string? nsReason = KubernetesNamespacePolicy.ExplainNamespace(options, requestedNamespace.Trim());
                if (nsReason != null) return Rejected(nsReason, summary);
            }
            return new EffectClassification(effect, reason, summary);
        }

        /// <summary>
        /// Build a rejection with an approval summary.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <param name="summary">Summary.</param>
        /// <returns>Classification.</returns>
        protected static EffectClassification Rejected(string reason, string summary)
        {
            EffectClassification c = EffectClassification.Reject(reason);
            c.ApprovalSummary = summary;
            return c;
        }

        /// <summary>
        /// Get the pooled session for the resource.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session. Do not dispose.</returns>
        protected static Task<KubernetesSession> GetSessionAsync(ConnectorInvocationContext context, KubernetesOptions options, CancellationToken token)
        {
            ConnectorResourceContext resource = context.Resource;
            return resource.Sessions.GetOrCreateAsync(resource.SessionKey(), ct => KubernetesSessionFactory.ConnectAsync(resource, options, ct), token);
        }

        /// <summary>
        /// Resolve a kind and enforce the namespace and cluster-scoped policy for it.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="options">Options.</param>
        /// <param name="kind">Kind or resource name.</param>
        /// <param name="apiVersion">apiVersion, or null.</param>
        /// <param name="requestedNamespace">Namespace argument, or null.</param>
        /// <param name="write">Whether the call changes the object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The type and the effective namespace (null for cluster-scoped types).</returns>
        /// <exception cref="ConnectorException">Thrown when the kind is unknown or the policy forbids the call.</exception>
        protected static async Task<KubernetesTarget> ResolveTargetAsync(KubernetesSession session, KubernetesOptions options, string kind, string? apiVersion, string? requestedNamespace, bool write, CancellationToken token)
        {
            KubernetesResourceType type = await session.Discovery.ResolveAsync(kind, apiVersion, token).ConfigureAwait(false);
            KubernetesTarget target = new KubernetesTarget();
            target.Type = type;
            if (type.Namespaced)
            {
                target.Namespace = KubernetesNamespacePolicy.Require(options, requestedNamespace);
            }
            else
            {
                KubernetesNamespacePolicy.RequireClusterScoped(options, type, write);
                target.Namespace = null;
            }
            return target;
        }

        /// <summary>
        /// Name used for the target in summaries: the binding alias, or the resource name.
        /// </summary>
        /// <param name="context">Context. May be null.</param>
        /// <returns>Name.</returns>
        protected static string TargetName(ConnectorInvocationContext? context)
        {
            if (context == null) return "the cluster";
            if (!String.IsNullOrEmpty(context.BindingAlias)) return context.BindingAlias;
            return String.IsNullOrEmpty(context.Resource.Name) ? "the cluster" : context.Resource.Name;
        }

        /// <summary>
        /// Read a trimmed optional string argument.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Name.</param>
        /// <returns>Value, or null when absent or blank.</returns>
        protected static string? Optional(ToolArguments arguments, string name)
        {
            string? v = arguments.GetString(name);
            if (String.IsNullOrWhiteSpace(v)) return null;
            return v.Trim();
        }

        /// <summary>
        /// Read a required string argument.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Name.</param>
        /// <returns>Value.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when absent.</exception>
        protected static string Required(ToolArguments arguments, string name)
        {
            string? v = Optional(arguments, name);
            if (v == null) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + name + "' is required.");
            return v;
        }

        /// <summary>
        /// Output byte budget for the call.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>Bytes.</returns>
        protected static int OutputBudget(ConnectorInvocationContext context)
        {
            return context.Limits.MaxBytes;
        }

        #endregion
    }
}
