namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Grafana dashboard UIDs used to build deep links (Phase 13 contract): one dashboard per section in the Hugo
    /// folder (P13.27).
    /// </summary>
    public class ObservabilityDashboards
    {
        /// <summary>
        /// Overview dashboard UID. Default hugo-overview.
        /// </summary>
        public string Overview { get; set; } = "hugo-overview";

        /// <summary>
        /// HTTP dashboard UID. Default hugo-http.
        /// </summary>
        public string Http { get; set; } = "hugo-http";

        /// <summary>
        /// Runs and scheduling dashboard UID. Default hugo-runs.
        /// </summary>
        public string Runs { get; set; } = "hugo-runs";

        /// <summary>
        /// Workers and harnesses dashboard UID. Default hugo-workers.
        /// </summary>
        public string Workers { get; set; } = "hugo-workers";

        /// <summary>
        /// Protocols dashboard UID. Default hugo-protocols.
        /// </summary>
        public string Protocols { get; set; } = "hugo-protocols";

        /// <summary>
        /// Integrations dashboard UID. Default hugo-integrations.
        /// </summary>
        public string Integrations { get; set; } = "hugo-integrations";

        /// <summary>
        /// Agents dashboard UID (per-agent invocations, run time percentiles, TTFT, streaming, queue time, guard trips). Default hugo-agents.
        /// </summary>
        public string Agents { get; set; } = "hugo-agents";

        /// <summary>
        /// Models and tokens dashboard UID (token breakdowns, cost, throughput by model). Default hugo-models.
        /// </summary>
        public string Models { get; set; } = "hugo-models";

        /// <summary>
        /// Kubernetes executor dashboard UID (run Jobs, pod start latency, pod failures). Default hugo-kubernetes.
        /// </summary>
        public string Kubernetes { get; set; } = "hugo-kubernetes";

        /// <summary>
        /// Scheduler and queue dashboard UID (queue depth, claims, leases, blocked runs). Default hugo-scheduler.
        /// </summary>
        public string Scheduler { get; set; } = "hugo-scheduler";

        /// <summary>
        /// Database dashboard UID (statement latency, errors, connections). Default hugo-database.
        /// </summary>
        public string Database { get; set; } = "hugo-database";

        /// <summary>
        /// Control plane dashboard UID (replicas, leader, backplane, background services). Default hugo-control-plane.
        /// </summary>
        public string ControlPlane { get; set; } = "hugo-control-plane";

        /// <summary>
        /// Security and auth dashboard UID (authentication failures, denials, rate limits, secrets). Default hugo-security.
        /// </summary>
        public string Security { get; set; } = "hugo-security";

        /// <summary>
        /// Logs and traces dashboard UID (log volume, errors and warnings, trace search). Default hugo-logs.
        /// </summary>
        public string Logs { get; set; } = "hugo-logs";
    }
}
