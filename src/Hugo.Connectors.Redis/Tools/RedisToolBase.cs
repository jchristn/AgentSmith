namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// Base for the Redis tools. Validates arguments before connecting, gets the pooled session, applies the call timeout,
    /// converts expected failures into tool errors (<c>timeout</c>, <c>auth_failed</c>, <c>connection_failed</c>, and the
    /// codes of <see cref="ConnectorException"/>), drops the pooled session after a connection failure, and lets
    /// cancellation by the caller propagate as <see cref="OperationCanceledException"/>. Thread-safe.
    /// </summary>
    public abstract class RedisToolBase : ConnectorToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected RedisToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when context or arguments is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the caller cancels the token.</exception>
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();

            Stopwatch sw = Stopwatch.StartNew();
            ToolResult result;
            try
            {
                RedisConnectionOptions options = RedisConnectionOptions.FromSettings(context.Resource.Settings);
                ValidateArguments(context, options, arguments);
            }
            catch (ConnectorException e)
            {
                result = ToolResult.Error(e.Code, e.Message);
                result.Metrics["latencyMs"] = sw.Elapsed.TotalMilliseconds;
                return result;
            }

            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(context.Timeout);
                try
                {
                    RedisSession session = await RedisSessionFactory.GetAsync(context.Resource, timeout.Token).ConfigureAwait(false);
                    result = await ExecuteCoreAsync(context, session, arguments, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    result = ToolResult.Error(
                        ConnectorErrorCodes.Timeout,
                        Descriptor.Name + " did not finish within " + context.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                        + " seconds. Narrow the request (smaller count, a more specific pattern) and try again.");
                }
                catch (ConnectorException e)
                {
                    if (e.Code == ConnectorErrorCodes.ConnectionFailed) RedisSessionFactory.Invalidate(context.Resource);
                    result = ToolResult.Error(e.Code, e.Message);
                }
                catch (Exception e) when (e is RedisException || e is System.Net.Sockets.SocketException || e is System.IO.IOException || e is System.Security.Authentication.AuthenticationException)
                {
                    ConnectorException mapped = RedisErrorMapper.Map(e, Descriptor.Name);
                    if (mapped.Code == ConnectorErrorCodes.ConnectionFailed || mapped.Code == ConnectorErrorCodes.AuthFailed) RedisSessionFactory.Invalidate(context.Resource);
                    result = ToolResult.Error(mapped.Code, mapped.Message);
                }
            }

            Scrub(context, result);
            result.Metrics["latencyMs"] = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Check arguments before any connection is made. Throw <see cref="ConnectorException"/> with
        /// <c>invalid_argument</c> (or <c>permission_denied</c>) to fail the call. Default: no checks.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="options">Resource options.</param>
        /// <param name="arguments">Arguments.</param>
        protected virtual void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
        }

        /// <summary>
        /// Execute the call. The token already includes the call timeout.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="session">Pooled session.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="token">Cancellation token including the timeout.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token);

        /// <summary>
        /// Run a command against the configured database (routed by key on a cluster when keys are passed as
        /// <see cref="RedisKey"/>).
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="command">Command name.</param>
        /// <param name="args">Arguments.</param>
        /// <param name="token">Cancellation token. The command keeps running on the server if cancelled; only the wait ends.</param>
        /// <returns>Reply.</returns>
        protected static Task<RedisResult> ExecAsync(RedisSession session, string command, IList<object> args, CancellationToken token)
        {
            return session.Database().ExecuteAsync(command, (ICollection<object>)args, CommandFlags.None).WaitAsync(token);
        }

        /// <summary>
        /// Read a required key argument.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Argument name. Default <c>key</c>.</param>
        /// <returns>Key text.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when absent.</exception>
        protected static string RequiredKey(ToolArguments arguments, string name = "key")
        {
            string? key = arguments.GetString(name);
            if (String.IsNullOrEmpty(key)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + name + "' is required: the Redis key name.");
            return key;
        }

        /// <summary>
        /// Read a list of keys from <c>keys</c> (array) or a single <c>key</c>.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="max">Most keys allowed.</param>
        /// <returns>Keys, without duplicates.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when none or too many are given.</exception>
        protected static List<string> KeyList(ToolArguments arguments, int max)
        {
            List<string> keys = new List<string>();
            foreach (string k in arguments.GetStringList("keys"))
            {
                if (!String.IsNullOrEmpty(k) && !keys.Contains(k)) keys.Add(k);
            }
            string? single = arguments.GetString("key");
            if (!String.IsNullOrEmpty(single) && !keys.Contains(single)) keys.Add(single);
            if (keys.Count == 0) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Give at least one key in 'keys'.");
            if (keys.Count > max) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "At most " + max + " keys are allowed per call; got " + keys.Count + ".");
            return keys;
        }

        /// <summary>
        /// Refuse a non-Read call on a ReadOnly binding (defense in depth; the server pipeline enforces this first).
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="effect">Effect of the call.</param>
        /// <exception cref="ConnectorException">Thrown with <c>permission_denied</c> when the binding is ReadOnly.</exception>
        protected static void RequireWritable(ConnectorInvocationContext context, EffectEnum effect)
        {
            if (effect != EffectEnum.Read && context.AccessMode == AccessModeEnum.ReadOnly)
                throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, "This binding is read-only, so a " + effect + " call cannot run.");
        }

        /// <summary>
        /// Quote a key for summaries: printable text in double quotes, cut to 80 characters.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Quoted key.</returns>
        protected static string Quote(string? key)
        {
            string k = (key ?? String.Empty).Replace("\n", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal);
            if (k.Length > 80) k = k.Substring(0, 77) + "...";
            return "\"" + k + "\"";
        }

        /// <summary>
        /// Replace the resource's resolved secret values in a result (for example a password echoed by
        /// <c>CONFIG GET requirepass</c>) with <c>[redacted]</c>. Defense in depth; the server scrubs outputs as well.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="result">Result to scrub in place.</param>
        protected static void Scrub(ConnectorInvocationContext context, ToolResult result)
        {
            List<string> secrets;
            try
            {
                secrets = context.Resource.Secrets.AllValues();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            foreach (string secret in secrets)
            {
                if (secret.Length < 4) continue;
                if (result.Text.Contains(secret, StringComparison.Ordinal)) result.Text = result.Text.Replace(secret, "[redacted]", StringComparison.Ordinal);
                if (result.StructuredJson != null && result.StructuredJson.Contains(secret, StringComparison.Ordinal))
                    result.StructuredJson = result.StructuredJson.Replace(secret, "[redacted]", StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Target name for summaries: the binding alias, or the resource name.
        /// </summary>
        /// <param name="context">Context, or null.</param>
        /// <returns>Target.</returns>
        protected static string Target(ConnectorInvocationContext? context)
        {
            if (context == null) return "the Redis resource";
            return String.IsNullOrEmpty(context.BindingAlias) ? context.Resource.Name : context.BindingAlias;
        }

        #endregion
    }
}
