namespace Hugo.Connectors.Docker
{
    using System;
    using global::Docker.DotNet.Models;

    /// <summary>
    /// Captures the first stats sample reported through <see cref="IProgress{T}"/> synchronously. Thread-safe.
    /// </summary>
    public class DockerStatsCollector : IProgress<ContainerStatsResponse>
    {
        #region Public-Members

        /// <summary>
        /// The first sample, or null when none arrived.
        /// </summary>
        public ContainerStatsResponse? Sample
        {
            get
            {
                lock (_Lock)
                {
                    return _Sample;
                }
            }
        }

        #endregion

        #region Private-Members

        private ContainerStatsResponse? _Sample = null;
        private readonly object _Lock = new object();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a sample; only the first is kept.
        /// </summary>
        /// <param name="value">Sample.</param>
        public void Report(ContainerStatsResponse value)
        {
            if (value == null) return;
            lock (_Lock)
            {
                if (_Sample == null) _Sample = value;
            }
        }

        #endregion
    }
}
