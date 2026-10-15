namespace Hugo.Core.Inference
{
    using System;

    /// <summary>
    /// Process-wide gateway settings. Per-endpoint settings (concurrency, timeout, retries) live on
    /// <see cref="ModelEndpointConfig"/>.
    /// </summary>
    public class ModelGatewaySettings
    {
        #region Public-Members

        /// <summary>
        /// Longest wait for a concurrency slot, in seconds. Default 60. Minimum 0, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public double QueueTimeoutSeconds
        {
            get
            {
                return _QueueTimeoutSeconds;
            }
            set
            {
                if (value < 0 || value > 3600) throw new ArgumentOutOfRangeException(nameof(QueueTimeoutSeconds), "QueueTimeoutSeconds must be between 0 and 3600.");
                _QueueTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Consecutive retryable failures that open an endpoint's circuit. Default 5. Minimum 1, maximum 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int CircuitFailureThreshold
        {
            get
            {
                return _CircuitFailureThreshold;
            }
            set
            {
                if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(CircuitFailureThreshold), "CircuitFailureThreshold must be between 1 and 1000.");
                _CircuitFailureThreshold = value;
            }
        }

        /// <summary>
        /// Seconds an open circuit stays open. Default 30. Minimum 0.01, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public double CircuitOpenSeconds
        {
            get
            {
                return _CircuitOpenSeconds;
            }
            set
            {
                if (value < 0.01 || value > 3600) throw new ArgumentOutOfRangeException(nameof(CircuitOpenSeconds), "CircuitOpenSeconds must be between 0.01 and 3600.");
                _CircuitOpenSeconds = value;
            }
        }

        /// <summary>
        /// Base backoff delay in milliseconds. Default 500. Minimum 1, maximum 60,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int RetryBaseDelayMs
        {
            get
            {
                return _RetryBaseDelayMs;
            }
            set
            {
                if (value < 1 || value > 60_000) throw new ArgumentOutOfRangeException(nameof(RetryBaseDelayMs), "RetryBaseDelayMs must be between 1 and 60,000.");
                _RetryBaseDelayMs = value;
            }
        }

        /// <summary>
        /// Largest backoff delay in milliseconds. Default 20,000. Minimum 1, maximum 600,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int RetryMaxDelayMs
        {
            get
            {
                return _RetryMaxDelayMs;
            }
            set
            {
                if (value < 1 || value > 600_000) throw new ArgumentOutOfRangeException(nameof(RetryMaxDelayMs), "RetryMaxDelayMs must be between 1 and 600,000.");
                _RetryMaxDelayMs = value;
            }
        }

        /// <summary>
        /// Largest Retry-After honored in seconds; a longer one skips to the fallback. Default 60. Minimum 0,
        /// maximum 3600.
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

        /// <summary>
        /// Prompt sent when validating an inference endpoint. Default asks for the word <c>pong</c>.
        /// </summary>
        public string ValidationPrompt
        {
            get
            {
                return _ValidationPrompt;
            }
            set
            {
                _ValidationPrompt = String.IsNullOrWhiteSpace(value) ? "Reply with the single word: pong" : value;
            }
        }

        /// <summary>
        /// Text embedded when validating an embedding endpoint. Default <c>Hugo endpoint validation</c>.
        /// </summary>
        public string ValidationEmbedText
        {
            get
            {
                return _ValidationEmbedText;
            }
            set
            {
                _ValidationEmbedText = String.IsNullOrWhiteSpace(value) ? "Hugo endpoint validation" : value;
            }
        }

        #endregion

        #region Private-Members

        private double _QueueTimeoutSeconds = 60;
        private int _CircuitFailureThreshold = 5;
        private double _CircuitOpenSeconds = 30;
        private int _RetryBaseDelayMs = 500;
        private int _RetryMaxDelayMs = 20_000;
        private double _MaxRetryAfterSeconds = 60;
        private string _ValidationPrompt = "Reply with the single word: pong";
        private string _ValidationEmbedText = "Hugo endpoint validation";

        #endregion
    }
}
