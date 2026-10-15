namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// A callback registered with <see cref="HugoMetrics.RegisterGauge(MetricDescriptor, Func{IEnumerable{Measurement{double}}})"/>.
    /// Dispose it to stop reporting. Thread-safe.
    /// </summary>
    public class GaugeRegistration : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Measurement callback.
        /// </summary>
        public Func<IEnumerable<Measurement<double>>> Observe { get; }

        #endregion

        #region Private-Members

        private readonly Action<GaugeRegistration> _Remove;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Instrument name.</param>
        /// <param name="observe">Measurement callback.</param>
        /// <param name="remove">Called once on dispose.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public GaugeRegistration(string name, Func<IEnumerable<Measurement<double>>> observe, Action<GaugeRegistration> remove)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Observe = observe ?? throw new ArgumentNullException(nameof(observe));
            _Remove = remove ?? throw new ArgumentNullException(nameof(remove));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose: the callback stops reporting.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Remove(this);
            _Disposed = true;
        }

        #endregion
    }
}
