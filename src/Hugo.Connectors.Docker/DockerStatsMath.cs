namespace Hugo.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using global::Docker.DotNet.Models;

    /// <summary>
    /// The calculations <c>docker stats</c> performs on a stats sample: CPU percent from the CPU and system deltas,
    /// memory use without the page cache, and network and block I/O totals. Thread-safe.
    /// </summary>
    public static class DockerStatsMath
    {
        #region Public-Methods

        /// <summary>
        /// CPU use as a percentage of one CPU (so 200 means two full CPUs), or 0 when there is no previous sample.
        /// </summary>
        /// <param name="s">Sample.</param>
        /// <returns>Percent.</returns>
        public static double CpuPercent(ContainerStatsResponse s)
        {
            ArgumentNullException.ThrowIfNull(s);
            if (s.CPUStats?.CPUUsage == null || s.PreCPUStats?.CPUUsage == null) return 0;
            double cpuDelta = (double)s.CPUStats.CPUUsage.TotalUsage - s.PreCPUStats.CPUUsage.TotalUsage;
            double systemDelta = (double)(s.CPUStats.SystemUsage ?? 0) - (s.PreCPUStats.SystemUsage ?? 0);
            double cpus = s.CPUStats.OnlineCPUs ?? (uint)(s.CPUStats.CPUUsage.PercpuUsage?.Count ?? 0);
            if (cpus <= 0) cpus = 1;
            if (cpuDelta <= 0 || systemDelta <= 0) return 0;
            return cpuDelta / systemDelta * cpus * 100.0;
        }

        /// <summary>
        /// Memory in use without the reclaimable page cache (inactive_file on cgroup v2, total_inactive_file or cache
        /// on cgroup v1), as the Docker CLI reports it.
        /// </summary>
        /// <param name="s">Sample.</param>
        /// <returns>Bytes.</returns>
        public static double MemoryUsed(ContainerStatsResponse s)
        {
            ArgumentNullException.ThrowIfNull(s);
            double usage = s.MemoryStats?.Usage ?? 0;
            IDictionary<string, ulong>? stats = s.MemoryStats?.Stats;
            if (stats != null)
            {
                if (stats.TryGetValue("inactive_file", out ulong inactive) && inactive < usage) return usage - inactive;
                if (stats.TryGetValue("total_inactive_file", out ulong totalInactive) && totalInactive < usage) return usage - totalInactive;
                if (stats.TryGetValue("cache", out ulong cache) && cache < usage) return usage - cache;
            }
            return usage;
        }

        /// <summary>
        /// Memory limit in bytes, or 0 when unknown.
        /// </summary>
        /// <param name="s">Sample.</param>
        /// <returns>Bytes.</returns>
        public static double MemoryLimit(ContainerStatsResponse s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return s.MemoryStats?.Limit ?? 0;
        }

        /// <summary>
        /// Memory use as a percentage of the limit, or 0 when the limit is unknown.
        /// </summary>
        /// <param name="s">Sample.</param>
        /// <returns>Percent.</returns>
        public static double MemoryPercent(ContainerStatsResponse s)
        {
            double limit = MemoryLimit(s);
            return limit <= 0 ? 0 : MemoryUsed(s) / limit * 100.0;
        }

        /// <summary>
        /// Total bytes received over every network interface.
        /// </summary>
        /// <param name="s">Sample.</param>
        /// <returns>Bytes.</returns>
        public static double NetworkReceived(ContainerStatsResponse s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return s.Networks?.Values.Sum(n => (double)n.RxBytes) ?? 0;
        }

        /// <summary>
        /// Total bytes sent over every network interface.
        /// </summary>
        /// <param name="s">Sample.</param>
        /// <returns>Bytes.</returns>
        public static double NetworkSent(ContainerStatsResponse s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return s.Networks?.Values.Sum(n => (double)n.TxBytes) ?? 0;
        }

        /// <summary>
        /// Total block bytes for an operation (<c>read</c> or <c>write</c>).
        /// </summary>
        /// <param name="s">Sample.</param>
        /// <param name="op">Operation, case-insensitive.</param>
        /// <returns>Bytes.</returns>
        public static double BlockBytes(ContainerStatsResponse s, string op)
        {
            ArgumentNullException.ThrowIfNull(s);
            return s.BlkioStats?.IoServiceBytesRecursive?.Where(e => String.Equals(e.Op, op, StringComparison.OrdinalIgnoreCase)).Sum(e => (double)e.Value) ?? 0;
        }

        #endregion
    }
}
