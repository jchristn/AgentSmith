namespace Hugo.Connectors.Agents.A2A
{
    using System;

    /// <summary>
    /// Process-wide options of the <c>a2a-agent</c> connector, set by the host when it constructs the connector. Not
    /// thread-safe for mutation; set before the connector is used.
    /// </summary>
    public class A2AAgentConnectorOptions
    {
        #region Public-Members

        /// <summary>
        /// Wait between <c>GetTask</c> polls while a non-streaming delegation is still working. Minimum 50 milliseconds,
        /// maximum 60 seconds, default 500 milliseconds.
        /// </summary>
        public TimeSpan PollInterval
        {
            get
            {
                return _PollInterval;
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(50)) value = TimeSpan.FromMilliseconds(50);
                if (value > TimeSpan.FromSeconds(60)) value = TimeSpan.FromSeconds(60);
                _PollInterval = value;
            }
        }

        /// <summary>
        /// Most characters of one remote message, artifact, or description shown. Minimum 200, maximum 1000000, default
        /// 20000. The call's output limits apply on top.
        /// </summary>
        public int MaxTextChars
        {
            get
            {
                return _MaxTextChars;
            }
            set
            {
                _MaxTextChars = Math.Clamp(value, 200, 1000000);
            }
        }

        /// <summary>
        /// Upper bound for <see cref="A2AAgentConnector.TestConnectionAsync"/>, applied together with the resource
        /// timeout and the server's own test timeout. Minimum 1 second, maximum 10 minutes, default 20 seconds.
        /// </summary>
        public TimeSpan TestConnectionTimeout
        {
            get
            {
                return _TestConnectionTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(10)) value = TimeSpan.FromMinutes(10);
                _TestConnectionTimeout = value;
            }
        }

        /// <summary>
        /// How long a resolved agent card is reused before <c>a2a_get_card</c> or a new session fetches it again.
        /// Minimum 1 minute, maximum 1 day, default 15 minutes.
        /// </summary>
        public TimeSpan CardRefreshInterval
        {
            get
            {
                return _CardRefreshInterval;
            }
            set
            {
                if (value < TimeSpan.FromMinutes(1)) value = TimeSpan.FromMinutes(1);
                if (value > TimeSpan.FromDays(1)) value = TimeSpan.FromDays(1);
                _CardRefreshInterval = value;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _PollInterval = TimeSpan.FromMilliseconds(500);
        private int _MaxTextChars = 20000;
        private TimeSpan _TestConnectionTimeout = TimeSpan.FromSeconds(20);
        private TimeSpan _CardRefreshInterval = TimeSpan.FromMinutes(15);

        #endregion
    }
}
