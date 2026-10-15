namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Concurrent;

    /// <summary>
    /// Per-endpoint circuit breaker (AssistantHub <c>UtilityCircuitBreaker</c> pattern, plan 6.4). After
    /// <see cref="FailureThreshold"/> consecutive retryable failures the circuit opens for <see cref="OpenSeconds"/>,
    /// during which calls fail fast (or go to the fallback endpoint). When the cooldown passes, the next call is a
    /// trial: success closes the circuit, failure reopens it immediately. In-process state only; it is rebuildable
    /// and not needed for correctness (D-027). Thread-safe.
    /// </summary>
    public class CircuitBreaker
    {
        #region Public-Members

        /// <summary>
        /// Consecutive failures that open the circuit. Default 5. Minimum 1, maximum 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int FailureThreshold
        {
            get
            {
                return _FailureThreshold;
            }
            set
            {
                if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(FailureThreshold), "FailureThreshold must be between 1 and 1000.");
                _FailureThreshold = value;
            }
        }

        /// <summary>
        /// Seconds the circuit stays open. Default 30. Minimum 0.01, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public double OpenSeconds
        {
            get
            {
                return _OpenSeconds;
            }
            set
            {
                if (value < 0.01 || value > 3600) throw new ArgumentOutOfRangeException(nameof(OpenSeconds), "OpenSeconds must be between 0.01 and 3600.");
                _OpenSeconds = value;
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, CircuitBreakerState> _States = new ConcurrentDictionary<string, CircuitBreakerState>(StringComparer.Ordinal);
        private int _FailureThreshold = 5;
        private double _OpenSeconds = 30;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the circuit for a key is open now.
        /// </summary>
        /// <param name="key">Endpoint key.</param>
        /// <returns>True when open.</returns>
        public bool IsOpen(string key)
        {
            if (String.IsNullOrEmpty(key) || !_States.TryGetValue(key, out CircuitBreakerState? state)) return false;
            lock (state) return DateTime.UtcNow < state.OpenUntilUtc;
        }

        /// <summary>
        /// Seconds until an open circuit allows a trial, or 0 when closed.
        /// </summary>
        /// <param name="key">Endpoint key.</param>
        /// <returns>Seconds remaining.</returns>
        public double SecondsUntilTrial(string key)
        {
            if (String.IsNullOrEmpty(key) || !_States.TryGetValue(key, out CircuitBreakerState? state)) return 0;
            lock (state) return Math.Max(0, (state.OpenUntilUtc - DateTime.UtcNow).TotalSeconds);
        }

        /// <summary>
        /// Record a success; closes the circuit.
        /// </summary>
        /// <param name="key">Endpoint key.</param>
        public void RecordSuccess(string key)
        {
            if (String.IsNullOrEmpty(key) || !_States.TryGetValue(key, out CircuitBreakerState? state)) return;
            lock (state)
            {
                state.ConsecutiveFailures = 0;
                state.OpenUntilUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Record a retryable failure.
        /// </summary>
        /// <param name="key">Endpoint key.</param>
        /// <returns>True when this failure opened the circuit.</returns>
        public bool RecordFailure(string key)
        {
            if (String.IsNullOrEmpty(key)) return false;
            CircuitBreakerState state = _States.GetOrAdd(key, _ => new CircuitBreakerState());
            lock (state)
            {
                state.ConsecutiveFailures++;
                if (state.ConsecutiveFailures < _FailureThreshold) return false;
                state.ConsecutiveFailures = _FailureThreshold - 1;
                state.OpenUntilUtc = DateTime.UtcNow.AddSeconds(_OpenSeconds);
                state.TimesOpened++;
                return true;
            }
        }

        /// <summary>
        /// Forget every circuit.
        /// </summary>
        public void Reset()
        {
            _States.Clear();
        }

        #endregion
    }
}
