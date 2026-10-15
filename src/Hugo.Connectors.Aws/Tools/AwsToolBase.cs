namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// Base of every <c>aws_*</c> tool. Enforces the resource's <c>allowedServices</c> at classification (rejected) and at
    /// execution (<c>permission_denied</c>), resolves the pooled session, resolves the caller identity once per session
    /// for approval summaries, applies the call timeout, and maps SDK failures to stable error codes. A cancellation
    /// requested by the caller propagates as <see cref="OperationCanceledException"/>; the call timeout returns a
    /// <c>timeout</c> error. Thread-safe.
    /// </summary>
    public abstract class AwsToolBase : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Service this tool calls (one of <see cref="AwsServiceNames"/>).
        /// </summary>
        public string Service { get; }

        /// <summary>
        /// IAM actions this tool can call, for example <c>ec2:DescribeInstances</c>. Used for classification reasons and
        /// the IAM policy documentation.
        /// </summary>
        public IReadOnlyList<string> Operations { get; }

        /// <summary>
        /// Longest time spent resolving the caller identity on a session's first use. Default 5 seconds, minimum 1 second,
        /// maximum 60 seconds.
        /// </summary>
        public TimeSpan IdentityTimeout
        {
            get
            {
                return _IdentityTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromSeconds(60)) value = TimeSpan.FromSeconds(60);
                _IdentityTimeout = value;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _IdentityTimeout = TimeSpan.FromSeconds(5);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Descriptor.</param>
        /// <param name="service">Service name.</param>
        /// <param name="operations">IAM actions the tool calls.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        protected AwsToolBase(ToolDescriptor descriptor, string service, params string[] operations) : base(descriptor)
        {
            if (String.IsNullOrEmpty(service)) throw new ArgumentNullException(nameof(service));
            ArgumentNullException.ThrowIfNull(operations);
            Service = service;
            Operations = operations.ToList();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            AwsOptions options = AwsOptions.FromSettings(context.Resource.Settings);
            if (!options.IsServiceAllowed(Service))
                return EffectClassification.Reject("service '" + Service + "' is not in this resource's allowedServices (" + String.Join(", ", options.AllowedServices) + ")");
            return ClassifyCore(context, arguments, options);
        }

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();

            AwsOptions options = AwsOptions.FromSettings(context.Resource.Settings);
            if (!options.IsServiceAllowed(Service))
                return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "Service '" + Service + "' is not allowed for resource " + context.Resource.Name + ". Allowed services: " + String.Join(", ", options.AllowedServices) + ".");

            Stopwatch stopwatch = Stopwatch.StartNew();
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(context.Timeout);
                try
                {
                    AwsSession session = await AwsSessionFactory.GetSessionAsync(context.Resource, cts.Token).ConfigureAwait(false);
                    await EnsureIdentityAsync(context.Resource, session, cts.Token).ConfigureAwait(false);
                    ToolResult result = await ExecuteCoreAsync(context, arguments, session, options, cts.Token).ConfigureAwait(false);
                    result.Metrics["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
                    return result;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ToolResult.Error(
                        ConnectorErrorCodes.Timeout,
                        Descriptor.Name + " timed out after " + context.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds. Narrow the request (fewer resources, a shorter time window) and retry.");
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    ConnectorException mapped = AwsErrorMapper.Map(e, Operations.Count > 0 ? Operations[0] : Descriptor.Name);
                    if (mapped.Code == ConnectorErrorCodes.AuthFailed || mapped.Code == ConnectorErrorCodes.ConnectionFailed)
                    {
                        AwsSessionFactory.InvalidateSession(context.Resource);
                        AwsIdentityCache.Remove(context.Resource);
                    }
                    if (!(e is ConnectorException)) context.Resource.Logger.Warn(Descriptor.Name + " failed on " + context.Resource.Name + ": " + mapped.Code + " (" + e.GetType().Name + ")");
                    ToolResult error = ToolResult.Error(mapped.Code, mapped.Message);
                    error.RetryAfterSeconds = mapped.RetryAfterSeconds;
                    return error;
                }
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Classify a call whose service is allowed. The default returns the base effect with a summary naming the account,
        /// region, and binding.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Resource options.</param>
        /// <returns>Classification.</returns>
        protected virtual EffectClassification ClassifyCore(ConnectorInvocationContext context, ToolArguments arguments, AwsOptions options)
        {
            string reason = Descriptor.Name + " calls " + String.Join(", ", Operations) + " (" + Descriptor.BaseEffect + ")";
            return new EffectClassification(Descriptor.BaseEffect, reason, Summarize(context, arguments));
        }

        /// <inheritdoc />
        protected override string Summarize(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string title = String.IsNullOrEmpty(Descriptor.Title) ? Descriptor.Name : Descriptor.Title;
            return title + " in " + Where(context);
        }

        /// <summary>
        /// Execute the call with a resolved session.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="session">Pooled session; do not dispose.</param>
        /// <param name="options">Resource options.</param>
        /// <param name="token">Cancellation token that also fires at the call timeout.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token);

        /// <summary>
        /// Describe where a call runs for approval summaries, for example
        /// <c>account 123456789012, region us-east-1 (aws-prod)</c>. The account comes from the cached caller identity; when
        /// it is not yet known the text says so.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <returns>Text.</returns>
        protected static string Where(ConnectorInvocationContext? context)
        {
            if (context == null) return "AWS";
            AwsOptions options = AwsOptions.FromSettings(context.Resource.Settings);
            AwsCallerIdentity? identity = AwsIdentityCache.Get(context.Resource);
            string account = identity == null || String.IsNullOrEmpty(identity.Account) ? "account not yet confirmed" : "account " + identity.Account;
            string alias = String.IsNullOrEmpty(context.BindingAlias) ? context.Resource.Name : context.BindingAlias;
            string text = account + ", region " + options.Region;
            return String.IsNullOrEmpty(alias) ? text : text + " (" + alias + ")";
        }

        /// <summary>
        /// Render a table with the session's region and account.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="session">Session.</param>
        /// <param name="table">Table.</param>
        /// <returns>Result.</returns>
        protected ToolResult Render(ConnectorInvocationContext context, AwsSession session, AwsTable table)
        {
            ArgumentNullException.ThrowIfNull(session);
            string? account = session.Identity?.Account ?? AwsIdentityCache.Get(context.Resource)?.Account;
            return AwsResultBuilder.Render(context, Descriptor.Name, session.Options.Region, account, table);
        }

        /// <summary>
        /// Page size for a call: the <c>maxResults</c> argument, else the resource default, clamped to the service range and
        /// the call's row limit.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Options.</param>
        /// <param name="minimum">Smallest value the AWS API accepts.</param>
        /// <param name="maximum">Largest value the AWS API accepts.</param>
        /// <returns>Page size.</returns>
        protected static int PageSize(ConnectorInvocationContext context, ToolArguments arguments, AwsOptions options, int minimum, int maximum)
        {
            int requested = arguments.GetInt("maxResults", options.MaxResults, 1, 100000);
            int limited = Math.Min(requested, context.Limits.MaxRows);
            return Math.Clamp(limited, minimum, maximum);
        }

        /// <summary>
        /// Join values for a table cell, or null when empty.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <param name="separator">Separator. Default comma and space.</param>
        /// <returns>Text or null.</returns>
        protected static string? Join(IEnumerable<string?>? values, string separator = ", ")
        {
            if (values == null) return null;
            List<string> list = values.Where(v => !String.IsNullOrEmpty(v)).Select(v => v!).ToList();
            return list.Count == 0 ? null : String.Join(separator, list);
        }

        /// <summary>
        /// Shorten text to a maximum length with an ellipsis.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="max">Maximum characters (at least 8).</param>
        /// <returns>Text, or null.</returns>
        protected static string? Shorten(string? value, int max)
        {
            if (value == null) return null;
            if (max < 8) max = 8;
            string v = value.Replace('\r', ' ').Replace('\n', ' ');
            return v.Length > max ? v.Substring(0, max - 3) + "..." : v;
        }

        #endregion

        #region Private-Methods

        private async Task EnsureIdentityAsync(ConnectorResourceContext resource, AwsSession session, CancellationToken token)
        {
            if (session.Identity != null) return;
            AwsCallerIdentity? cached = AwsIdentityCache.Get(resource);
            if (cached != null)
            {
                session.Identity = cached;
                return;
            }

            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(_IdentityTimeout);
                try
                {
                    await AwsSessionFactory.ResolveIdentityAsync(resource, session, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    session.Identity = new AwsCallerIdentity { Region = session.Options.Region };
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    resource.Logger.Debug("aws caller identity unavailable for " + resource.Name + ": " + e.GetType().Name);
                    session.Identity = new AwsCallerIdentity { Region = session.Options.Region };
                }
            }
        }

        #endregion
    }
}
