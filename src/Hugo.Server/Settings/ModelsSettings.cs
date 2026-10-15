namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Model endpoint settings (used from Phase 6).
    /// </summary>
    public class ModelsSettings
    {
        #region Public-Members

        /// <summary>
        /// Seconds between model endpoint health checks; 0 turns them off. Maximum 86400, default 300.
        /// </summary>
        public int HealthIntervalSeconds
        {
            get
            {
                return _HealthIntervalSeconds;
            }
            set
            {
                _HealthIntervalSeconds = Math.Clamp(value, 0, 86400);
            }
        }

        #endregion

        #region Private-Members

        private int _HealthIntervalSeconds = 300;

        #endregion
    }
}
