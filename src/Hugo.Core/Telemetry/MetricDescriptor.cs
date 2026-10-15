namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using Radiant;

    /// <summary>
    /// One Hugo metric in the telemetry catalog (P13.20): its OpenTelemetry name, kind, unit, labels, the process
    /// that emits it, the coverage category it belongs to, and the Grafana dashboard that shows it. The catalog drives
    /// the histogram buckets, docs/TELEMETRY.md, and the coverage tests. Not thread-safe; build once.
    /// </summary>
    public class MetricDescriptor
    {
        #region Public-Members

        /// <summary>
        /// OpenTelemetry instrument name, for example hugo.runs.created. Prometheus replaces dots with underscores
        /// and adds _total to counters and the unit suffix to histograms in seconds or bytes.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Instrument kind. Default counter.
        /// </summary>
        public MetricKindEnum Kind { get; set; } = MetricKindEnum.Counter;

        /// <summary>
        /// UCUM unit: s for seconds, By for bytes, or a curly-brace annotation such as {run}. Null for none.
        /// </summary>
        public string? Unit { get; set; } = null;

        /// <summary>
        /// Label keys the metric carries. Never null.
        /// </summary>
        public List<string> Labels
        {
            get
            {
                return _Labels;
            }
            set
            {
                _Labels = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Histogram bucket boundaries, ascending, or null for the SDK default (histograms only).
        /// </summary>
        public double[]? Buckets { get; set; } = null;

        /// <summary>
        /// What the metric measures.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Process that emits the metric: server, worker, or server and worker.
        /// </summary>
        public string Process { get; set; } = "server";

        /// <summary>
        /// Coverage category, for example HTTP, Runs, Protocols, Database. Matches the coverage matrix sections.
        /// </summary>
        public string Category { get; set; } = String.Empty;

        /// <summary>
        /// UID of the Grafana dashboard that shows the metric, for example hugo-runs.
        /// </summary>
        public string Dashboard { get; set; } = String.Empty;

        /// <summary>
        /// Prometheus series name a dashboard queries: the base name for counters (with _total), histograms (with the
        /// unit suffix, then _bucket, _sum, _count), and gauges.
        /// </summary>
        public string PrometheusName
        {
            get
            {
                string name = _Name.Replace('.', '_');
                if (Kind == MetricKindEnum.Histogram)
                {
                    if (Unit == "s") name += "_seconds";
                    else if (Unit == "By") name += "_bytes";
                }
                if (Kind == MetricKindEnum.Counter || Kind == MetricKindEnum.ObservableCounter) name += "_total";
                return name;
            }
        }

        #endregion

        #region Private-Members

        private string _Name = "hugo.unnamed";
        private List<string> _Labels = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MetricDescriptor()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Instrument name.</param>
        /// <param name="kind">Kind.</param>
        /// <param name="unit">Unit, or null.</param>
        /// <param name="labels">Label keys, comma separated, or empty.</param>
        /// <param name="description">Description.</param>
        /// <param name="process">Emitting process.</param>
        /// <param name="category">Coverage category.</param>
        /// <param name="dashboard">Grafana dashboard UID.</param>
        /// <param name="buckets">Histogram buckets, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        public MetricDescriptor(string name, MetricKindEnum kind, string? unit, string labels, string description, string process, string category, string dashboard, double[]? buckets = null)
        {
            Name = name;
            Kind = kind;
            Unit = unit;
            if (!String.IsNullOrEmpty(labels))
            {
                foreach (string label in labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) _Labels.Add(label);
            }
            Description = description ?? String.Empty;
            Process = process ?? "server";
            Category = category ?? String.Empty;
            Dashboard = dashboard ?? String.Empty;
            Buckets = buckets;
        }

        #endregion
    }
}
