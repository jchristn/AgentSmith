namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Writes tool invocation records of executed calls in the background through a bounded channel (plan section 8.6
    /// step 11), so recording never slows a call. Denied and approval rows are written synchronously by the pipeline,
    /// not here. When the channel is full, the writer waits rather than drop a record. Thread-safe.
    /// </summary>
    public class ToolInvocationRecorder : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Records queued and not yet written.
        /// </summary>
        public int Pending
        {
            get
            {
                return Volatile.Read(ref _Pending);
            }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly HugoLog _Logging;
        private readonly Channel<ToolInvocation> _Channel;
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private Task? _Writer = null;
        private int _Pending = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="logging">Logging.</param>
        /// <param name="capacity">Channel capacity. Minimum 16, maximum 1000000. Default 10000.</param>
        /// <exception cref="ArgumentNullException">Thrown when database or logging is null.</exception>
        public ToolInvocationRecorder(DatabaseDriverBase database, HugoLog logging, int capacity = 10000)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            BoundedChannelOptions options = new BoundedChannelOptions(Math.Clamp(capacity, 16, 1000000));
            options.FullMode = BoundedChannelFullMode.Wait;
            options.SingleReader = true;
            _Channel = Channel.CreateBounded<ToolInvocation>(options);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the background writer.
        /// </summary>
        public void Start()
        {
            if (_Writer == null) _Writer = Task.Run(() => WriteLoopAsync(_Stopping.Token));
        }

        /// <summary>
        /// Queue a new record.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task completing when queued.</returns>
        public async Task EnqueueAsync(ToolInvocation record, CancellationToken token)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (_Writer == null)
            {
                await _Database.ToolInvocations.CreateAsync(record, token).ConfigureAwait(false);
                return;
            }
            Interlocked.Increment(ref _Pending);
            await _Channel.Writer.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait until every queued record is written (tests, shutdown).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task FlushAsync(CancellationToken token)
        {
            while (Volatile.Read(ref _Pending) > 0)
            {
                await Task.Delay(10, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stop the writer after draining the queue.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(CancellationToken token)
        {
            _Channel.Writer.TryComplete();
            if (_Writer != null)
            {
                try
                {
                    await _Writer.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _Stopping.Cancel();
                }
            }
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
        /// Stop the writer.
        /// </summary>
        /// <param name="disposing">Whether called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Channel.Writer.TryComplete();
                _Stopping.Cancel();
                _Stopping.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task WriteLoopAsync(CancellationToken token)
        {
            try
            {
                while (await _Channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_Channel.Reader.TryRead(out ToolInvocation? record))
                    {
                        try
                        {
                            await _Database.ToolInvocations.CreateAsync(record, token).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            _Logging.Warn("[ToolInvocations] writing record " + record.Id + " failed: " + SecretScrubber.Shared.Scrub(e.Message));
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _Pending);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        #endregion
    }
}
