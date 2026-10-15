namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Linq;
    using System.Net;

    /// <summary>
    /// In-process Papertrail API stub replaying the shapes recorded in docs/connectors/logging-api-notes.md:
    /// <c>/api/v1/events/search.json</c> (with <c>q</c>, <c>system_id</c>, <c>group_id</c>, <c>min_id</c>,
    /// <c>max_id</c>, <c>min_time</c>, <c>max_time</c>, <c>limit</c>, and the <c>reached_*</c> flags),
    /// <c>systems.json</c>, <c>groups.json</c>, and <c>searches.json</c>. Authenticates with
    /// <c>X-Papertrail-Token</c> and sends <c>X-Rate-Limit-*</c> headers. Thread-safe.
    /// </summary>
    public class PapertrailStub : LoggingStubServer
    {
        #region Public-Members

        /// <summary>
        /// Base URL to configure on a <c>papertrail</c> resource.
        /// </summary>
        public string ApiBaseUrl
        {
            get
            {
                return BaseAddress + "/api/v1";
            }
        }

        /// <summary>
        /// Snapshot of the events, oldest first.
        /// </summary>
        public List<StubLogEvent> Events
        {
            get
            {
                lock (_Lock)
                {
                    return _Events.ToList();
                }
            }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<StubLogEvent> _Events;
        private static readonly TimeSpan _OwnerOffset = TimeSpan.FromHours(-7);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start the stub with a generated data set.
        /// </summary>
        /// <param name="eventCount">Number of events. Default 300.</param>
        /// <param name="intervalSeconds">Seconds between events. Default 30.</param>
        public PapertrailStub(int eventCount = 300, int intervalSeconds = 30)
        {
            _Events = StubLogEvent.Generate(eventCount, intervalSeconds, DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append an event received now.
        /// </summary>
        /// <param name="host">web-01, web-02, or db-01.</param>
        /// <param name="message">Message.</param>
        /// <returns>The event.</returns>
        public StubLogEvent AddEvent(string host, string message)
        {
            lock (_Lock)
            {
                DateTime now = DateTime.UtcNow;
                int index = host == "web-01" ? 0 : (host == "web-02" ? 1 : 2);
                StubLogEvent e = new StubLogEvent
                {
                    Id = _Events.Max(x => x.Id) + 7,
                    Time = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
                    Host = host,
                    SystemId = 101 + index,
                    EntityId = "e-100" + (index + 1),
                    Group = index == 2 ? "prod-db" : "prod-web",
                    Program = index == 2 ? "postgres" : "nginx",
                    Severity = "Info",
                    Message = message
                };
                _Events.Add(e);
                return e;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool IsAuthorized(HttpListenerRequest request)
        {
            return String.Equals(request.Headers["X-Papertrail-Token"], ApiToken, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        protected override object UnauthorizedBody()
        {
            return new Dictionary<string, object> { { "message", "Authentication required" } };
        }

        /// <inheritdoc />
        protected override Dictionary<string, string> CommonHeaders()
        {
            return new Dictionary<string, string>
            {
                { "X-Rate-Limit-Limit", "25" },
                { "X-Rate-Limit-Remaining", "24" },
                { "X-Rate-Limit-Reset", "5" }
            };
        }

        /// <inheritdoc />
        protected override StubResponse Handle(string path, NameValueCollection query)
        {
            switch (path)
            {
                case "/api/v1/events/search.json":
                    return Search(query);
                case "/api/v1/systems.json":
                    return new StubResponse(200, Systems());
                case "/api/v1/groups.json":
                    return new StubResponse(200, Groups());
                case "/api/v1/searches.json":
                    return new StubResponse(200, SavedSearches());
                default:
                    return new StubResponse(404, new Dictionary<string, object> { { "message", "Not Found" } });
            }
        }

        #endregion

        #region Private-Methods

        private StubResponse Search(NameValueCollection query)
        {
            List<StubLogEvent> all = Events;
            string? q = query["q"];
            string? systemId = query["system_id"];
            string? groupId = query["group_id"];
            long? minId = ParseLong(query["min_id"]);
            long? maxId = ParseLong(query["max_id"]);
            long? minTime = ParseLong(query["min_time"]);
            long? maxTime = ParseLong(query["max_time"]);
            int limit = (int)Math.Clamp(ParseLong(query["limit"]) ?? 1000, 1, 10000);

            HashSet<long>? groupSystems = null;
            if (!String.IsNullOrEmpty(groupId))
            {
                if (groupId == "31") groupSystems = new HashSet<long> { 101, 102, 103 };
                else if (groupId == "32") groupSystems = new HashSet<long> { 101, 102 };
                else return new StubResponse(400, new Dictionary<string, object> { { "message", "Group not found" } });
            }

            string? term = String.IsNullOrWhiteSpace(q) ? null : q.Trim().Trim('"');
            List<StubLogEvent> matches = all.Where(e =>
                (term == null || e.Message.Contains(term, StringComparison.OrdinalIgnoreCase) || e.Program.Contains(term, StringComparison.OrdinalIgnoreCase))
                && (String.IsNullOrEmpty(systemId) || systemId == e.SystemId.ToString(CultureInfo.InvariantCulture) || String.Equals(systemId, e.Host.Replace('-', '_'), StringComparison.OrdinalIgnoreCase) || String.Equals(systemId, e.Host, StringComparison.OrdinalIgnoreCase))
                && (groupSystems == null || groupSystems.Contains(e.SystemId))
                && (!minId.HasValue || e.Id > minId.Value)
                && (!maxId.HasValue || e.Id < maxId.Value)
                && (!minTime.HasValue || Epoch(e.Time) >= minTime.Value)
                && (!maxTime.HasValue || Epoch(e.Time) <= maxTime.Value))
                .OrderBy(e => e.Id)
                .ToList();

            List<StubLogEvent> page = matches.Skip(Math.Max(0, matches.Count - limit)).ToList();
            bool recordLimit = matches.Count >= limit;
            bool beginning = !minTime.HasValue && matches.Count <= limit;

            Dictionary<string, object?> body = new Dictionary<string, object?>
            {
                { "min_id", page.Any() ? page.First().Id.ToString(CultureInfo.InvariantCulture) : (maxId.HasValue ? maxId.Value.ToString(CultureInfo.InvariantCulture) : "0") },
                { "max_id", page.Any() ? page.Last().Id.ToString(CultureInfo.InvariantCulture) : (minId.HasValue ? minId.Value.ToString(CultureInfo.InvariantCulture) : "0") },
                { "reached_beginning", beginning },
                { "reached_record_limit", recordLimit },
                { "events", page.Select(e => EventJson(e)).ToList() }
            };
            if (page.Any())
            {
                body["min_time_at"] = Local(page.First().Time);
                body["max_time_at"] = Local(page.Last().Time);
            }
            return new StubResponse(200, body);
        }

        private static Dictionary<string, object?> EventJson(StubLogEvent e)
        {
            return new Dictionary<string, object?>
            {
                { "id", e.Id.ToString(CultureInfo.InvariantCulture) },
                { "generated_at", Local(e.Time) },
                { "received_at", Local(e.Time) },
                { "display_received_at", e.Time.Add(_OwnerOffset).ToString("MMM dd HH:mm:ss", CultureInfo.InvariantCulture) },
                { "source_name", e.Host },
                { "source_id", e.SystemId },
                { "hostname", e.Host },
                { "source_ip", "10.0.0." + (e.SystemId - 90) },
                { "facility", "Local7" },
                { "severity", e.Severity },
                { "program", e.Program },
                { "message", e.Message }
            };
        }

        private static List<Dictionary<string, object?>> Systems()
        {
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            string[] names = new string[] { "web-01", "web-02", "db-01" };
            for (int i = 0; i < names.Length; i++)
            {
                list.Add(new Dictionary<string, object?>
                {
                    { "id", 101 + i },
                    { "name", names[i] },
                    { "last_event_at", Local(DateTime.UtcNow.AddSeconds(-10)) },
                    { "ip_address", "10.0.0." + (11 + i) },
                    { "hostname", null },
                    { "syslog", new Dictionary<string, object> { { "hostname", "logs.papertrailapp.com" }, { "port", 514 } } },
                    { "_links", Links("systems/" + (101 + i)) }
                });
            }
            return list;
        }

        private static List<Dictionary<string, object?>> Groups()
        {
            return new List<Dictionary<string, object?>>
            {
                new Dictionary<string, object?>
                {
                    { "id", 31 },
                    { "name", "All Systems" },
                    { "system_wildcard", "*" },
                    { "systems", new List<Dictionary<string, object>> { SystemRef(101, "web-01"), SystemRef(102, "web-02"), SystemRef(103, "db-01") } },
                    { "_links", Links("groups/31") }
                },
                new Dictionary<string, object?>
                {
                    { "id", 32 },
                    { "name", "Web Servers" },
                    { "system_wildcard", "web-*" },
                    { "systems", new List<Dictionary<string, object>> { SystemRef(101, "web-01"), SystemRef(102, "web-02") } },
                    { "_links", Links("groups/32") }
                }
            };
        }

        private static List<Dictionary<string, object?>> SavedSearches()
        {
            return new List<Dictionary<string, object?>>
            {
                new Dictionary<string, object?>
                {
                    { "id", 1111 },
                    { "name", "Upstream errors" },
                    { "query", "\"upstream timeout\"" },
                    { "group", new Dictionary<string, object> { { "id", 32 }, { "name", "Web Servers" }, { "_links", Links("groups/32") } } },
                    { "_links", Links("searches/1111") }
                },
                new Dictionary<string, object?>
                {
                    { "id", 1112 },
                    { "name", "Database" },
                    { "query", "postgres" },
                    { "group", new Dictionary<string, object> { { "id", 31 }, { "name", "All Systems" }, { "_links", Links("groups/31") } } },
                    { "_links", Links("searches/1112") }
                }
            };
        }

        private static Dictionary<string, object> SystemRef(long id, string name)
        {
            return new Dictionary<string, object> { { "id", id }, { "name", name } };
        }

        private static Dictionary<string, object> Links(string self)
        {
            return new Dictionary<string, object>
            {
                { "self", new Dictionary<string, string> { { "href", "https://papertrailapp.com/api/v1/" + self + ".json" } } },
                { "html", new Dictionary<string, string> { { "href", "https://papertrailapp.com/" + self } } }
            };
        }

        private static string Local(DateTime utc)
        {
            return new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(_OwnerOffset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        private static long Epoch(DateTime utc)
        {
            return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        private static long? ParseLong(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            return Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;
        }

        #endregion
    }
}
