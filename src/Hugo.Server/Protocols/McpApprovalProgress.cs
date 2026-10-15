namespace Hugo.Server.Protocols
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Voltaic.Mcp;

    /// <summary>
    /// Sends MCP progress notifications while a tool call waits for an approval (P7.7, plan section 11.4): one when
    /// the wait starts and one every interval after, until the approval is decided or the call returns. Nothing is sent
    /// when the client supplied no progress token. Thread-safe; dispose to stop.
    /// </summary>
    public class McpApprovalProgress : IAgentApprovalListener, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Progress notifications sent so far.
        /// </summary>
        public int Sent
        {
            get
            {
                return Volatile.Read(ref _Sent);
            }
        }

        #endregion

        #region Private-Members

        private readonly McpToolCallContext? _Call;
        private readonly TimeSpan _Interval;
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly long _Started = Stopwatch.GetTimestamp();
        private readonly object _Lock = new object();
        private Task? _Loop = null;
        private double _Last = -1;
        private int _Sent = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="call">The tool call (McpToolCallContext.Current), or null outside a tool call.</param>
        /// <param name="interval">Interval between notifications. Minimum 1 second.</param>
        public McpApprovalProgress(McpToolCallContext? call, TimeSpan interval)
        {
            _Call = call;
            _Interval = interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : interval;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start notifying for an approval.
        /// </summary>
        /// <param name="approvalId">Approval identifier.</param>
        /// <param name="summary">Approval summary.</param>
        public void Start(string approvalId, string summary)
        {
            if (_Call == null || !_Call.ProgressToken.HasValue) return;
            lock (_Lock)
            {
                if (_Loop != null || _Disposed) return;
                string message = "Waiting for a human to decide approval " + approvalId + ": " + summary;
                _Loop = Task.Run(() => LoopAsync(message, _Stop.Token));
            }
        }

        /// <inheritdoc />
        public Task ApprovalRequiredAsync(AgentApprovalRequest approval, CancellationToken token)
        {
            if (approval != null) Start(approval.ApprovalId, approval.Summary);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ApprovalResolvedAsync(AgentApprovalResolution resolution, CancellationToken token)
        {
            Cancel();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Cancel();
                lock (_Lock)
                {
                    _Disposed = true;
                }
                _Stop.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void Cancel()
        {
            try
            {
                _Stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task LoopAsync(string message, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await SendAsync(message, token).ConfigureAwait(false);
                try
                {
                    await Task.Delay(_Interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task SendAsync(string message, CancellationToken token)
        {
            if (_Call == null) return;
            double progress = Math.Round(Stopwatch.GetElapsedTime(_Started).TotalSeconds, 1);
            lock (_Lock)
            {
                if (progress <= _Last) progress = _Last + 0.1;
                _Last = progress;
            }
            try
            {
                await _Call.ReportProgressAsync(progress, null, message, token).ConfigureAwait(false);
                Interlocked.Increment(ref _Sent);
            }
            catch (Exception e) when (e is OperationCanceledException || e is ArgumentOutOfRangeException || e is InvalidOperationException || e is ObjectDisposedException || e is System.IO.IOException)
            {
                // Progress is best effort; the call's result is what matters.
            }
        }

        #endregion
    }
}
