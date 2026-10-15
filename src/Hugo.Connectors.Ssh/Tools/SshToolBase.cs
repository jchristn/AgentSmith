namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Renci.SshNet;

    /// <summary>
    /// Base for SSH tools: option parsing, pooled sessions, remote command execution with error mapping, and conversion
    /// of <see cref="ConnectorException"/> to error results. Thread-safe; derived tools keep no per-call state in fields.
    /// </summary>
    public abstract class SshToolBase : ConnectorToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Descriptor.</param>
        protected SshToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public sealed override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();
            SshConnectionOptions options = SshConnectionOptions.FromSettings(context.Resource.Settings);
            try
            {
                return await ExecuteCoreAsync(context, arguments, options, token).ConfigureAwait(false);
            }
            catch (ConnectorException e)
            {
                return ToolResult.Error(e.Code, e.Message);
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Execute the tool.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Parsed resource options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token);

        /// <summary>
        /// Get a pooled session for the resource, reconnecting when the pooled one has dropped.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session. Do not dispose.</returns>
        protected static async Task<SshSession> GetSessionAsync(ConnectorInvocationContext context, SshConnectionOptions options, CancellationToken token)
        {
            ConnectorResourceContext resource = context.Resource;
            string key = resource.SessionKey();
            SshSession session = await resource.Sessions.GetOrCreateAsync(key, ct => SshSessionFactory.ConnectAsync(resource, options, ct), token).ConfigureAwait(false);
            if (session.IsConnected) return session;
            resource.Sessions.Invalidate(key);
            return await resource.Sessions.GetOrCreateAsync(key, ct => SshSessionFactory.ConnectAsync(resource, options, ct), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the pooled session's SFTP client.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>SFTP client. Do not dispose.</returns>
        protected static async Task<SftpClient> GetSftpAsync(ConnectorInvocationContext context, SshConnectionOptions options, CancellationToken token)
        {
            SshSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            try
            {
                return await session.GetSftpAsync(token).ConfigureAwait(false);
            }
            catch (ConnectorException e) when (e.Code == "connection_failed")
            {
                context.Resource.Sessions.Invalidate(context.Resource.SessionKey());
                throw;
            }
        }

        /// <summary>
        /// Run a remote command on the pooled session. A broken connection invalidates the session.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <param name="commandText">Remote command line.</param>
        /// <param name="timeout">Timeout.</param>
        /// <param name="captureBytes">Bytes kept per stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ConnectorException">Thrown when the session cannot run the command.</exception>
        protected static async Task<SshCommandResult> RunAsync(ConnectorInvocationContext context, SshConnectionOptions options, string commandText, TimeSpan timeout, int captureBytes, CancellationToken token)
        {
            SshSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            try
            {
                return await SshCommandRunner.RunAsync(session.Client, commandText, timeout, captureBytes, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                if (SshErrorMapper.IsConnectionBroken(e)) context.Resource.Sessions.Invalidate(context.Resource.SessionKey());
                throw SshErrorMapper.MapOperation(e, "command");
            }
        }

        /// <summary>
        /// Map an SFTP failure, invalidating the session when the connection broke.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="e">Exception.</param>
        /// <param name="path">Path being accessed.</param>
        /// <returns>Connector exception to throw.</returns>
        protected static ConnectorException MapSftp(ConnectorInvocationContext context, Exception e, string path)
        {
            if (SshErrorMapper.IsConnectionBroken(e)) context.Resource.Sessions.Invalidate(context.Resource.SessionKey());
            return SshErrorMapper.MapOperation(e, path);
        }

        /// <summary>
        /// Effective timeout: the smaller of the requested seconds (when given), the resource's
        /// <c>commandTimeoutSeconds</c>, and the context timeout.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <param name="requestedSeconds">Requested seconds, or null.</param>
        /// <returns>Timeout.</returns>
        protected static TimeSpan EffectiveTimeout(ConnectorInvocationContext context, SshConnectionOptions options, int? requestedSeconds)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(options.CommandTimeoutSeconds);
            if (requestedSeconds.HasValue && requestedSeconds.Value > 0 && TimeSpan.FromSeconds(requestedSeconds.Value) < timeout) timeout = TimeSpan.FromSeconds(requestedSeconds.Value);
            if (context.Timeout < timeout) timeout = context.Timeout;
            return timeout;
        }

        /// <summary>
        /// Output byte budget: the smaller of the resource's <c>maxOutputBytes</c> and the context limit.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <returns>Bytes.</returns>
        protected static int OutputBudget(ConnectorInvocationContext context, SshConnectionOptions options)
        {
            return Math.Min(options.MaxOutputBytes, context.Limits.MaxBytes);
        }

        /// <summary>
        /// Name used for the target in summaries: the binding alias, or the resource name.
        /// </summary>
        /// <param name="context">Context. May be null.</param>
        /// <returns>Name.</returns>
        protected static string TargetName(ConnectorInvocationContext? context)
        {
            if (context == null) return "the host";
            if (!String.IsNullOrEmpty(context.BindingAlias)) return context.BindingAlias;
            return String.IsNullOrEmpty(context.Resource.Name) ? "the host" : context.Resource.Name;
        }

        /// <summary>
        /// Shorten text for summaries.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="max">Maximum characters.</param>
        /// <returns>Shortened text.</returns>
        protected static string Shorten(string? value, int max)
        {
            string v = (value ?? String.Empty).Replace('\n', ' ').Replace('\r', ' ');
            return v.Length > max ? v.Substring(0, Math.Max(0, max - 3)) + "..." : v;
        }

        /// <summary>
        /// Convert a resolved path for SFTP, which has no tilde expansion: <c>~</c> becomes <c>.</c> and <c>~/x</c>
        /// becomes <c>x</c> (SFTP resolves relative paths from the login directory).
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>SFTP path.</returns>
        protected static string SftpPath(string path)
        {
            if (path == "~") return ".";
            if (path.StartsWith("~/", StringComparison.Ordinal)) return path.Length == 2 ? "." : path.Substring(2);
            return path;
        }

        #endregion
    }
}
