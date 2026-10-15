namespace Hugo.Core.Inference
{
    using System.Threading;

    /// <summary>
    /// Per-call HTTP facts captured by <see cref="RetryAfterCaptureHandler"/> beneath PolyPrompt, which does not
    /// surface response headers. The gateway installs a fresh instance in <see cref="Current"/> (an async-local)
    /// before each attempt and reads it afterwards. Thread-safe for the single writer and reader of one attempt.
    /// </summary>
    public class ModelCallContext
    {
        #region Public-Members

        /// <summary>
        /// The context of the current async flow, or null when none is installed.
        /// </summary>
        public static ModelCallContext? Current
        {
            get
            {
                return _Current.Value;
            }
            set
            {
                _Current.Value = value;
            }
        }

        /// <summary>
        /// HTTP status of the last response seen in this flow. Null when no response arrived.
        /// </summary>
        public int? LastStatusCode
        {
            get
            {
                lock (_Lock) return _LastStatusCode;
            }
            set
            {
                lock (_Lock) _LastStatusCode = value;
            }
        }

        /// <summary>
        /// Retry-After of the last response, in seconds. Null when the header was absent or unparseable.
        /// </summary>
        public double? RetryAfterSeconds
        {
            get
            {
                lock (_Lock) return _RetryAfterSeconds;
            }
            set
            {
                lock (_Lock) _RetryAfterSeconds = value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly AsyncLocal<ModelCallContext?> _Current = new AsyncLocal<ModelCallContext?>();
        private readonly object _Lock = new object();
        private int? _LastStatusCode = null;
        private double? _RetryAfterSeconds = null;

        #endregion
    }
}
