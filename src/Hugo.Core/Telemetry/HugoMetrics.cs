namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using Radiant;

    /// <summary>
    /// The Hugo meter (P13.20 to P13.24): one process-wide <see cref="Meter"/> named <c>Hugo</c> holding an
    /// instrument for every descriptor in <see cref="HugoMetricCatalog"/>, with recording helpers that fold label
    /// values through the cardinality guard. Radiant subscribes to the meter when telemetry is enabled; with nothing
    /// listening a measurement costs a few nanoseconds. Recording never throws. Thread-safe.
    /// </summary>
    public static class HugoMetrics
    {
        #region Public-Members

        /// <summary>
        /// Meter name the telemetry pipeline subscribes to: Hugo.
        /// </summary>
        public static readonly string MeterName = "Hugo";

        /// <summary>
        /// Cardinality guard applied to every label value. Configure <see cref="LabelGuard.MaxLabelValues"/> from
        /// Telemetry.MaxLabelValues at startup.
        /// </summary>
        public static LabelGuard Labels
        {
            get
            {
                return _Labels;
            }
        }

        /// <summary>
        /// The meter.
        /// </summary>
        public static Meter Meter
        {
            get
            {
                return _Meter;
            }
        }

        #endregion

        #region Private-Members

        private static readonly Meter _Meter = new Meter("Hugo", Constants.ProductVersion);
        private static readonly LabelGuard _Labels = new LabelGuard();
        private static readonly Dictionary<string, Counter<double>> _Counters = new Dictionary<string, Counter<double>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Histogram<double>> _Histograms = new Dictionary<string, Histogram<double>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, UpDownCounter<double>> _UpDown = new Dictionary<string, UpDownCounter<double>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<GaugeRegistration>> _Gauges = new Dictionary<string, List<GaugeRegistration>>(StringComparer.Ordinal);
        private static readonly object _GaugeLock = new object();

        #endregion

        #region Constructors-and-Factories

        static HugoMetrics()
        {
            foreach (MetricDescriptor descriptor in HugoMetricCatalog.All())
            {
                switch (descriptor.Kind)
                {
                    case MetricKindEnum.Counter:
                        _Counters[descriptor.Name] = _Meter.CreateCounter<double>(descriptor.Name, descriptor.Unit, descriptor.Description);
                        break;
                    case MetricKindEnum.Histogram:
                        _Histograms[descriptor.Name] = _Meter.CreateHistogram<double>(descriptor.Name, descriptor.Unit, descriptor.Description);
                        break;
                    case MetricKindEnum.UpDownCounter:
                        _UpDown[descriptor.Name] = _Meter.CreateUpDownCounter<double>(descriptor.Name, descriptor.Unit, descriptor.Description);
                        break;
                    case MetricKindEnum.ObservableGauge:
                        string name = descriptor.Name;
                        _Gauges[name] = new List<GaugeRegistration>();
                        _Meter.CreateObservableGauge<double>(name, () => ObserveGauge(name), descriptor.Unit, descriptor.Description);
                        break;
                }
            }
            _Labels.OnFolded = key =>
            {
                if (_Counters.TryGetValue(HugoMetricCatalog.LabelsFolded.Name, out Counter<double>? folded)) folded.Add(1, new KeyValuePair<string, object?>("label", key));
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fold a label value through the cardinality guard.
        /// </summary>
        /// <param name="key">Label key.</param>
        /// <param name="value">Value.</param>
        /// <returns>Reported value.</returns>
        public static string Fold(string key, string? value)
        {
            return _Labels.Fold(key, value);
        }

        /// <summary>
        /// Build tags with folded values.
        /// </summary>
        /// <param name="k1">Key.</param>
        /// <param name="v1">Value.</param>
        /// <returns>Tags.</returns>
        public static TagList Tags(string k1, string? v1)
        {
            TagList tags = new TagList();
            tags.Add(k1, _Labels.Fold(k1, v1));
            return tags;
        }

        /// <summary>
        /// Build tags with folded values.
        /// </summary>
        /// <param name="k1">Key 1.</param>
        /// <param name="v1">Value 1.</param>
        /// <param name="k2">Key 2.</param>
        /// <param name="v2">Value 2.</param>
        /// <returns>Tags.</returns>
        public static TagList Tags(string k1, string? v1, string k2, string? v2)
        {
            TagList tags = Tags(k1, v1);
            tags.Add(k2, _Labels.Fold(k2, v2));
            return tags;
        }

        /// <summary>
        /// Build tags with folded values.
        /// </summary>
        /// <param name="k1">Key 1.</param>
        /// <param name="v1">Value 1.</param>
        /// <param name="k2">Key 2.</param>
        /// <param name="v2">Value 2.</param>
        /// <param name="k3">Key 3.</param>
        /// <param name="v3">Value 3.</param>
        /// <returns>Tags.</returns>
        public static TagList Tags(string k1, string? v1, string k2, string? v2, string k3, string? v3)
        {
            TagList tags = Tags(k1, v1, k2, v2);
            tags.Add(k3, _Labels.Fold(k3, v3));
            return tags;
        }

        /// <summary>
        /// Build tags with folded values.
        /// </summary>
        /// <param name="k1">Key 1.</param>
        /// <param name="v1">Value 1.</param>
        /// <param name="k2">Key 2.</param>
        /// <param name="v2">Value 2.</param>
        /// <param name="k3">Key 3.</param>
        /// <param name="v3">Value 3.</param>
        /// <param name="k4">Key 4.</param>
        /// <param name="v4">Value 4.</param>
        /// <returns>Tags.</returns>
        public static TagList Tags(string k1, string? v1, string k2, string? v2, string k3, string? v3, string k4, string? v4)
        {
            TagList tags = Tags(k1, v1, k2, v2, k3, v3);
            tags.Add(k4, _Labels.Fold(k4, v4));
            return tags;
        }


        /// <summary>
        /// Add to a counter.
        /// </summary>
        /// <param name="metric">Counter descriptor.</param>
        /// <param name="value">Non-negative amount.</param>
        /// <param name="tags">Tags.</param>
        public static void Count(MetricDescriptor metric, double value, in TagList tags)
        {
            if (metric == null || value < 0) return;
            try
            {
                if (_Counters.TryGetValue(metric.Name, out Counter<double>? counter) && counter.Enabled) counter.Add(value, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Increment a counter with no labels.
        /// </summary>
        /// <param name="metric">Counter descriptor.</param>
        public static void Count(MetricDescriptor metric)
        {
            Count(metric, 1, new TagList());
        }

        /// <summary>
        /// Increment a counter with one label.
        /// </summary>
        /// <param name="metric">Counter descriptor.</param>
        /// <param name="k1">Key.</param>
        /// <param name="v1">Value.</param>
        public static void Count(MetricDescriptor metric, string k1, string? v1)
        {
            if (!Enabled(metric)) return;
            Count(metric, 1, Tags(k1, v1));
        }

        /// <summary>
        /// Increment a counter with two labels.
        /// </summary>
        /// <param name="metric">Counter descriptor.</param>
        /// <param name="k1">Key 1.</param>
        /// <param name="v1">Value 1.</param>
        /// <param name="k2">Key 2.</param>
        /// <param name="v2">Value 2.</param>
        public static void Count(MetricDescriptor metric, string k1, string? v1, string k2, string? v2)
        {
            if (!Enabled(metric)) return;
            Count(metric, 1, Tags(k1, v1, k2, v2));
        }

        /// <summary>
        /// Increment a counter with three labels.
        /// </summary>
        /// <param name="metric">Counter descriptor.</param>
        /// <param name="k1">Key 1.</param>
        /// <param name="v1">Value 1.</param>
        /// <param name="k2">Key 2.</param>
        /// <param name="v2">Value 2.</param>
        /// <param name="k3">Key 3.</param>
        /// <param name="v3">Value 3.</param>
        public static void Count(MetricDescriptor metric, string k1, string? v1, string k2, string? v2, string k3, string? v3)
        {
            if (!Enabled(metric)) return;
            Count(metric, 1, Tags(k1, v1, k2, v2, k3, v3));
        }

        /// <summary>
        /// Record a histogram value.
        /// </summary>
        /// <param name="metric">Histogram descriptor.</param>
        /// <param name="value">Value.</param>
        /// <param name="tags">Tags.</param>
        public static void Observe(MetricDescriptor metric, double value, in TagList tags)
        {
            if (metric == null || Double.IsNaN(value) || Double.IsInfinity(value)) return;
            try
            {
                if (_Histograms.TryGetValue(metric.Name, out Histogram<double>? histogram) && histogram.Enabled) histogram.Record(value, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a histogram value with no labels.
        /// </summary>
        /// <param name="metric">Histogram descriptor.</param>
        /// <param name="value">Value.</param>
        public static void Observe(MetricDescriptor metric, double value)
        {
            Observe(metric, value, new TagList());
        }

        /// <summary>
        /// Record a histogram value with one label.
        /// </summary>
        /// <param name="metric">Histogram descriptor.</param>
        /// <param name="value">Value.</param>
        /// <param name="k1">Key.</param>
        /// <param name="v1">Value.</param>
        public static void Observe(MetricDescriptor metric, double value, string k1, string? v1)
        {
            if (!Enabled(metric)) return;
            Observe(metric, value, Tags(k1, v1));
        }

        /// <summary>
        /// Record a histogram value with two labels.
        /// </summary>
        /// <param name="metric">Histogram descriptor.</param>
        /// <param name="value">Value.</param>
        /// <param name="k1">Key 1.</param>
        /// <param name="v1">Value 1.</param>
        /// <param name="k2">Key 2.</param>
        /// <param name="v2">Value 2.</param>
        public static void Observe(MetricDescriptor metric, double value, string k1, string? v1, string k2, string? v2)
        {
            if (!Enabled(metric)) return;
            Observe(metric, value, Tags(k1, v1, k2, v2));
        }

        /// <summary>
        /// Record a histogram value with three labels.
        /// </summary>
        /// <param name="metric">Histogram descriptor.</param>
        /// <param name="value">Value.</param>
        /// <param name="k1">Key 1.</param>
        /// <param name="v1">Value 1.</param>
        /// <param name="k2">Key 2.</param>
        /// <param name="v2">Value 2.</param>
        /// <param name="k3">Key 3.</param>
        /// <param name="v3">Value 3.</param>
        public static void Observe(MetricDescriptor metric, double value, string k1, string? v1, string k2, string? v2, string k3, string? v3)
        {
            if (!Enabled(metric)) return;
            Observe(metric, value, Tags(k1, v1, k2, v2, k3, v3));
        }

        /// <summary>
        /// Add a delta (which may be negative) to an up/down counter.
        /// </summary>
        /// <param name="metric">Up/down counter descriptor.</param>
        /// <param name="delta">Delta.</param>
        /// <param name="tags">Tags.</param>
        public static void Adjust(MetricDescriptor metric, double delta, in TagList tags)
        {
            if (metric == null) return;
            try
            {
                if (_UpDown.TryGetValue(metric.Name, out UpDownCounter<double>? counter) && counter.Enabled) counter.Add(delta, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Whether anything listens to an instrument. Use it to skip building expensive tags.
        /// </summary>
        /// <param name="metric">Descriptor.</param>
        /// <returns>True when a listener is subscribed.</returns>
        public static bool Enabled(MetricDescriptor metric)
        {
            if (metric == null) return false;
            if (_Counters.TryGetValue(metric.Name, out Counter<double>? counter)) return counter.Enabled;
            if (_Histograms.TryGetValue(metric.Name, out Histogram<double>? histogram)) return histogram.Enabled;
            if (_UpDown.TryGetValue(metric.Name, out UpDownCounter<double>? upDown)) return upDown.Enabled;
            return false;
        }

        /// <summary>
        /// Register a callback that reports measurements for an observable gauge. Several callbacks can report the same
        /// gauge (for example several hosts in one test process); their measurements are concatenated.
        /// </summary>
        /// <param name="metric">Gauge descriptor.</param>
        /// <param name="observe">Callback; keep it fast and non-throwing.</param>
        /// <returns>Registration; dispose it to stop reporting.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the descriptor is not an observable gauge in the catalog.</exception>
        public static GaugeRegistration RegisterGauge(MetricDescriptor metric, Func<IEnumerable<Measurement<double>>> observe)
        {
            if (metric == null) throw new ArgumentNullException(nameof(metric));
            if (observe == null) throw new ArgumentNullException(nameof(observe));
            lock (_GaugeLock)
            {
                if (!_Gauges.TryGetValue(metric.Name, out List<GaugeRegistration>? list)) throw new ArgumentException("Metric " + metric.Name + " is not an observable gauge in the catalog.", nameof(metric));
                GaugeRegistration registration = new GaugeRegistration(metric.Name, observe, Unregister);
                list.Add(registration);
                return registration;
            }
        }

        /// <summary>
        /// Register a single-value callback for an observable gauge with no labels.
        /// </summary>
        /// <param name="metric">Gauge descriptor.</param>
        /// <param name="observe">Callback.</param>
        /// <returns>Registration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static GaugeRegistration RegisterGauge(MetricDescriptor metric, Func<double> observe)
        {
            if (observe == null) throw new ArgumentNullException(nameof(observe));
            return RegisterGauge(metric, () => new Measurement<double>[] { new Measurement<double>(observe()) });
        }

        /// <summary>
        /// A gauge measurement with one folded label.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="k1">Key.</param>
        /// <param name="v1">Value.</param>
        /// <returns>Measurement.</returns>
        public static Measurement<double> Measure(double value, string k1, string? v1)
        {
            return new Measurement<double>(value, new KeyValuePair<string, object?>(k1, _Labels.Fold(k1, v1)));
        }

        /// <summary>
        /// A gauge measurement with folded labels.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="tags">Tags built with <see cref="Tags(string, string?)"/> and friends.</param>
        /// <returns>Measurement.</returns>
        public static Measurement<double> Measure(double value, in TagList tags)
        {
            return new Measurement<double>(value, tags);
        }

        /// <summary>
        /// Seconds elapsed since a <see cref="Stopwatch.GetTimestamp"/> value.
        /// </summary>
        /// <param name="startTimestamp">Start timestamp.</param>
        /// <returns>Seconds.</returns>
        public static double SecondsSince(long startTimestamp)
        {
            return Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        }

        /// <summary>
        /// The status class of an HTTP status code: 2xx, 4xx, and so on; error for 0 or less.
        /// </summary>
        /// <param name="statusCode">Status code.</param>
        /// <returns>Status class.</returns>
        public static string StatusClass(int statusCode)
        {
            if (statusCode <= 0) return "error";
            return (statusCode / 100) + "xx";
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<double>> ObserveGauge(string name)
        {
            GaugeRegistration[] registrations;
            lock (_GaugeLock)
            {
                if (!_Gauges.TryGetValue(name, out List<GaugeRegistration>? list) || list.Count == 0) return Array.Empty<Measurement<double>>();
                registrations = list.ToArray();
            }
            List<Measurement<double>> measurements = new List<Measurement<double>>();
            foreach (GaugeRegistration registration in registrations)
            {
                try
                {
                    measurements.AddRange(registration.Observe());
                }
                catch (Exception)
                {
                }
            }
            return measurements;
        }

        private static void Unregister(GaugeRegistration registration)
        {
            lock (_GaugeLock)
            {
                if (_Gauges.TryGetValue(registration.Name, out List<GaugeRegistration>? list)) list.Remove(registration);
            }
        }

        #endregion
    }
}
