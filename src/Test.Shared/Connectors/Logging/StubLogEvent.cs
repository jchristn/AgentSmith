namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A synthetic log event served by the logging stubs.
    /// </summary>
    public class StubLogEvent
    {
        #region Public-Members

        /// <summary>
        /// Event ID. Increases with time but is not sequential.
        /// </summary>
        public long Id { get; set; } = 0;

        /// <summary>
        /// Event time, UTC, whole seconds.
        /// </summary>
        public DateTime Time { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Host (system) name.
        /// </summary>
        public string Host { get; set; } = String.Empty;

        /// <summary>
        /// Papertrail system ID.
        /// </summary>
        public long SystemId { get; set; } = 0;

        /// <summary>
        /// SolarWinds Observability entity ID.
        /// </summary>
        public string EntityId { get; set; } = String.Empty;

        /// <summary>
        /// SolarWinds Observability log group.
        /// </summary>
        public string Group { get; set; } = String.Empty;

        /// <summary>
        /// Program.
        /// </summary>
        public string Program { get; set; } = String.Empty;

        /// <summary>
        /// Severity.
        /// </summary>
        public string Severity { get; set; } = String.Empty;

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a deterministic data set: one event every <paramref name="intervalSeconds"/> seconds ending ten
        /// seconds before <paramref name="nowUtc"/>, rotating across web-01, web-02, and db-01. Every tenth event is
        /// an ERROR line.
        /// </summary>
        /// <param name="count">Number of events.</param>
        /// <param name="intervalSeconds">Seconds between events.</param>
        /// <param name="nowUtc">Current time, UTC.</param>
        /// <returns>Events, oldest first.</returns>
        public static List<StubLogEvent> Generate(int count, int intervalSeconds, DateTime nowUtc)
        {
            DateTime end = new DateTime(nowUtc.Ticks - (nowUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc).AddSeconds(-10);
            List<StubLogEvent> events = new List<StubLogEvent>();
            for (int i = 0; i < count; i++)
            {
                int host = i % 3;
                bool error = i % 10 == 0;
                events.Add(new StubLogEvent
                {
                    Id = 7700000000000000L + (i * 7L),
                    Time = end.AddSeconds(-(long)intervalSeconds * (count - 1 - i)),
                    Host = host == 0 ? "web-01" : (host == 1 ? "web-02" : "db-01"),
                    SystemId = 101 + host,
                    EntityId = "e-100" + (host + 1),
                    Group = host == 2 ? "prod-db" : "prod-web",
                    Program = host == 2 ? "postgres" : "nginx",
                    Severity = error ? "Error" : "Info",
                    Message = error ? "ERROR upstream timeout after 5000 ms (request " + i + ")" : "GET /api/items/" + i + " 200 12ms"
                });
            }
            return events;
        }

        #endregion
    }
}
