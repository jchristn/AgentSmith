namespace Hugo.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>Typed BenchmarkReport contract for the public API benchmark.</summary>
    public class BenchmarkReport
    {
        /// <summary>Status value.</summary>
        public string Status { get; set; } = "incomplete";
        /// <summary>Command value.</summary>
        public string Command { get; set; } = String.Empty;
        /// <summary>StartedUtc value.</summary>
        public string StartedUtc { get; set; } = DateTime.UtcNow.ToString("O");
        /// <summary>SourceCommit value.</summary>
        public string SourceCommit { get; set; } = String.Empty;
        /// <summary>ServerRevision value.</summary>
        public string ServerRevision { get; set; } = String.Empty;
        /// <summary>DatabaseProvider value.</summary>
        public string DatabaseProvider { get; set; } = String.Empty;
        /// <summary>OperatingSystem value.</summary>
        public string OperatingSystem { get; set; } = String.Empty;
        /// <summary>Architecture value.</summary>
        public string Architecture { get; set; } = String.Empty;
        /// <summary>LogicalProcessors value.</summary>
        public int LogicalProcessors { get; set; } = 0;
        /// <summary>Samples value.</summary>
        public int Samples { get; set; } = 0;
        /// <summary>HttpTimeoutSeconds value.</summary>
        public int HttpTimeoutSeconds { get; set; } = 60;
        /// <summary>MaxConnectionsPerServer value.</summary>
        public int MaxConnectionsPerServer { get; set; } = 4;
        /// <summary>Failures value.</summary>
        public int Failures { get; set; } = 0;
        /// <summary>TimingCaveat value.</summary>
        public string TimingCaveat { get; set; } = "Indicative local measurements. Do not compare different machines or runs with competing workloads.";
        /// <summary>Measurements value.</summary>
        public List<BenchmarkMeasurement> Measurements { get; set; } = new List<BenchmarkMeasurement>();
    }
}
