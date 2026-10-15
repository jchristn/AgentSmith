namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-process change notifications for A2A tasks, so streams and blocking SendMessage calls wake as soon as a task
    /// changes. The database stays the source of truth (D-027): waiters re-read the task and also wake on a timeout.
    /// Thread-safe.
    /// </summary>
    public class A2ATaskSignals
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _Signals = new ConcurrentDictionary<string, TaskCompletionSource<bool>>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wake everyone waiting on a task.
        /// </summary>
        /// <param name="taskId">Task identifier.</param>
        public void Notify(string taskId)
        {
            if (String.IsNullOrEmpty(taskId)) return;
            if (_Signals.TryRemove(taskId, out TaskCompletionSource<bool>? signal)) signal.TrySetResult(true);
        }

        /// <summary>
        /// Wait for the next change of a task, or the timeout.
        /// </summary>
        /// <param name="taskId">Task identifier.</param>
        /// <param name="timeout">Longest wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when signalled, false on timeout.</returns>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        public async Task<bool> WaitAsync(string taskId, TimeSpan timeout, CancellationToken token)
        {
            TaskCompletionSource<bool> signal = _Signals.GetOrAdd(taskId, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            Task finished = await Task.WhenAny(signal.Task, Task.Delay(timeout, token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return finished == signal.Task;
        }

        #endregion
    }
}
