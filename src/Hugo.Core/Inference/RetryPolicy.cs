namespace Hugo.Core.Inference
{
    using System;

    /// <summary>
    /// Retry decisions and delays for model calls (plan 6.4): 408, 429, and 5xx responses, transport errors, and
    /// timeouts are retryable. The delay honors Retry-After (capped at <see cref="MaxRetryAfterSeconds"/>), else it
    /// is exponential backoff from <see cref="BaseDelayMs"/>, capped at <see cref="MaxDelayMs"/>, with jitter that
    /// picks a random point between half and all of that delay.
    /// Thread-safe.
    /// </summary>
    public class RetryPolicy
    {
        #region Public-Members

        /// <summary>
        /// Base backoff delay in milliseconds. Default 500. Minimum 1, maximum 60,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int BaseDelayMs
        {
            get
            {
                return _BaseDelayMs;
            }
            set
            {
                if (value < 1 || value > 60_000) throw new ArgumentOutOfRangeException(nameof(BaseDelayMs), "BaseDelayMs must be between 1 and 60,000.");
                _BaseDelayMs = value;
            }
        }

        /// <summary>
        /// Largest backoff delay in milliseconds (without Retry-After). Default 20,000. Minimum 1, maximum 600,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int MaxDelayMs
        {
            get
            {
                return _MaxDelayMs;
            }
            set
            {
                if (value < 1 || value > 600_000) throw new ArgumentOutOfRangeException(nameof(MaxDelayMs), "MaxDelayMs must be between 1 and 600,000.");
                _MaxDelayMs = value;
            }
        }

        /// <summary>
        /// Largest Retry-After honored, in seconds. A larger advertised value is treated as non-retryable within
        /// this call (the gateway moves on to the fallback). Default 60. Minimum 0, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public double MaxRetryAfterSeconds
        {
            get
            {
                return _MaxRetryAfterSeconds;
            }
            set
            {
                if (value < 0 || value > 3600) throw new ArgumentOutOfRangeException(nameof(MaxRetryAfterSeconds), "MaxRetryAfterSeconds must be between 0 and 3600.");
                _MaxRetryAfterSeconds = value;
            }
        }

        #endregion

        #region Private-Members

        private int _BaseDelayMs = 500;
        private int _MaxDelayMs = 20_000;
        private double _MaxRetryAfterSeconds = 60;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an HTTP status is retryable.
        /// </summary>
        /// <param name="statusCode">Status code; null means a transport failure (retryable).</param>
        /// <returns>True when retryable.</returns>
        public static bool IsRetryableStatus(int? statusCode)
        {
            if (!statusCode.HasValue) return true;
            int s = statusCode.Value;
            return s == 408 || s == 429 || (s >= 500 && s <= 599);
        }

        /// <summary>
        /// Whether a Retry-After value is within what this policy will wait.
        /// </summary>
        /// <param name="retryAfterSeconds">Retry-After in seconds, or null.</param>
        /// <returns>True when absent or within <see cref="MaxRetryAfterSeconds"/>.</returns>
        public bool AcceptsRetryAfter(double? retryAfterSeconds)
        {
            return !retryAfterSeconds.HasValue || retryAfterSeconds.Value <= _MaxRetryAfterSeconds;
        }

        /// <summary>
        /// Delay before retry number <paramref name="retry"/> (1 for the first retry).
        /// </summary>
        /// <param name="retry">Retry number, 1-based.</param>
        /// <param name="retryAfterSeconds">Retry-After from the failed response, or null.</param>
        /// <returns>Delay.</returns>
        public TimeSpan ComputeDelay(int retry, double? retryAfterSeconds)
        {
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value >= 0)
                return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, _MaxRetryAfterSeconds));
            int exponent = Math.Clamp(retry - 1, 0, 20);
            double ceiling = Math.Min(_MaxDelayMs, _BaseDelayMs * Math.Pow(2, exponent));
            double jittered = ceiling / 2 + Random.Shared.NextDouble() * ceiling / 2;
            return TimeSpan.FromMilliseconds(jittered);
        }

        #endregion
    }
}
