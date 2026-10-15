namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// Reads and renders core/v1 events for <c>k8s_events</c> and <c>k8s_describe</c>. Thread-safe.
    /// </summary>
    public static class KubernetesEvents
    {
        #region Public-Methods

        /// <summary>
        /// List events in one namespace, or in all namespaces when <paramref name="ns"/> is null.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="ns">Namespace, or null.</param>
        /// <param name="fieldSelector">Field selector, or null.</param>
        /// <param name="limit">Most events fetched from the server.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events, newest first.</returns>
        /// <exception cref="ConnectorException">Thrown when the request fails.</exception>
        public static async Task<List<Corev1Event>> ListAsync(KubernetesSession session, string? ns, string? fieldSelector, int limit, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(session);
            string path = (String.IsNullOrEmpty(ns) ? "/api/v1/events" : "/api/v1/namespaces/" + Uri.EscapeDataString(ns) + "/events")
                + KubernetesSession.Query("fieldSelector", fieldSelector, "limit", KubernetesSession.Number(Math.Max(1, limit)));
            string body = await session.GetAsync(path, "events" + (String.IsNullOrEmpty(ns) ? String.Empty : " in namespace " + ns), token).ConfigureAwait(false);
            Corev1EventList list = KubernetesFormat.Typed<Corev1EventList>(body, "events");
            return (list.Items ?? new List<Corev1Event>()).Where(e => e != null).OrderByDescending(e => LastSeen(e) ?? DateTime.MinValue).ToList();
        }

        /// <summary>
        /// When an event was last seen: lastTimestamp, else eventTime, else series lastObservedTime, else creation.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <returns>Time (UTC), or null.</returns>
        public static DateTime? LastSeen(Corev1Event e)
        {
            ArgumentNullException.ThrowIfNull(e);
            DateTime? t = e.LastTimestamp ?? e.Series?.LastObservedTime ?? e.EventTime ?? e.FirstTimestamp ?? e.Metadata?.CreationTimestamp;
            return t?.ToUniversalTime();
        }

        /// <summary>
        /// Render events as a table.
        /// </summary>
        /// <param name="events">Events.</param>
        /// <param name="includeNamespace">Whether to add a namespace column.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <returns>Table text.</returns>
        public static string Render(IList<Corev1Event> events, bool includeNamespace, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(events);
            List<string> columns = new List<string>();
            if (includeNamespace) columns.Add("NAMESPACE");
            columns.AddRange(new[] { "LAST SEEN", "TYPE", "REASON", "OBJECT", "COUNT", "MESSAGE" });
            List<IList<string?>> rows = new List<IList<string?>>();
            foreach (Corev1Event e in events)
            {
                List<string?> row = new List<string?>();
                if (includeNamespace) row.Add(e.Metadata?.NamespaceProperty ?? e.InvolvedObject?.NamespaceProperty ?? String.Empty);
                row.Add(KubernetesFormat.Age(LastSeen(e), now));
                row.Add(e.Type ?? String.Empty);
                row.Add(e.Reason ?? String.Empty);
                row.Add((e.InvolvedObject?.Kind ?? "?").ToLowerInvariant() + "/" + (e.InvolvedObject?.Name ?? "?"));
                row.Add((e.Count ?? e.Series?.Count ?? 1).ToString(CultureInfo.InvariantCulture));
                row.Add(KubernetesFormat.Shorten(e.Message, 300));
                rows.Add(row);
            }
            return TextTable.Render(columns, rows, 300);
        }

        #endregion
    }
}
