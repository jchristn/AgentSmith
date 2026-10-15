namespace Hugo.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using global::Docker.DotNet.Models;

    /// <summary>
    /// Collects daemon events reported through <see cref="IProgress{T}"/> synchronously (unlike
    /// <see cref="Progress{T}"/>, which posts callbacks), keeping at most a fixed number and cancelling the read once
    /// it is full. Thread-safe.
    /// </summary>
    public class DockerEventCollector : IProgress<Message>
    {
        #region Public-Members

        /// <summary>
        /// Most events kept. Minimum 1.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Whether more events arrived than were kept.
        /// </summary>
        public bool Overflowed
        {
            get
            {
                lock (_Lock)
                {
                    return _Overflowed;
                }
            }
        }

        #endregion

        #region Private-Members

        private readonly List<Message> _Events = new List<Message>();
        private readonly CancellationTokenSource? _StopWhenFull;
        private readonly object _Lock = new object();
        private bool _Overflowed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="capacity">Most events kept; values below 1 become 1.</param>
        /// <param name="stopWhenFull">Cancelled when one more event than the capacity arrives, or null.</param>
        public DockerEventCollector(int capacity, CancellationTokenSource? stopWhenFull)
        {
            Capacity = Math.Max(1, capacity);
            _StopWhenFull = stopWhenFull;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record an event.
        /// </summary>
        /// <param name="value">Event.</param>
        public void Report(Message value)
        {
            if (value == null) return;
            bool stop = false;
            lock (_Lock)
            {
                if (_Events.Count < Capacity) _Events.Add(value);
                else
                {
                    _Overflowed = true;
                    stop = true;
                }
            }
            if (stop && _StopWhenFull != null)
            {
                try
                {
                    _StopWhenFull.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /// <summary>
        /// The kept events, oldest first.
        /// </summary>
        /// <returns>Copy of the events.</returns>
        public List<Message> Events()
        {
            lock (_Lock)
            {
                return new List<Message>(_Events);
            }
        }

        #endregion
    }
}
