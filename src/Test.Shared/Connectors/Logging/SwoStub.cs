namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Text;

    /// <summary>
    /// In-process SolarWinds Observability public API stub replaying the shapes of OpenAPI 1.0.19 as recorded in
    /// docs/connectors/logging-api-notes.md: <c>/v1/logs</c> (with <c>pageInfo.nextPage</c> returned as a link to
    /// exercise both token forms), <c>/v1/entities</c> (plain token), <c>/v1/entities/{id}</c>, and
    /// <c>/v1/metrics/{name}/measurements</c>. Authenticates with a bearer token. Thread-safe.
    /// </summary>
    public class SwoStub : LoggingStubServer
    {
        #region Public-Members

        /// <summary>
        /// Base URL to configure as <c>baseUrl</c> on a <c>solarwinds-observability</c> resource.
        /// </summary>
        public string ApiBaseUrl
        {
            get
            {
                return BaseAddress + "/v1";
            }
        }

        /// <summary>
        /// Metric names the stub knows.
        /// </summary>
        public static readonly string KnownMetric = "system.cpu.utilization";

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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start the stub with a generated data set.
        /// </summary>
        /// <param name="eventCount">Number of events. Default 300.</param>
        /// <param name="intervalSeconds">Seconds between events. Default 30.</param>
        public SwoStub(int eventCount = 300, int intervalSeconds = 30)
        {
            _Events = StubLogEvent.Generate(eventCount, intervalSeconds, DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append an event at the current second.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>The event.</returns>
        public StubLogEvent AddEvent(string message)
        {
            lock (_Lock)
            {
                DateTime now = DateTime.UtcNow;
                StubLogEvent e = new StubLogEvent
                {
                    Id = _Events.Max(x => x.Id) + 7,
                    Time = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
                    Host = "web-01",
                    SystemId = 101,
                    EntityId = "e-1001",
                    Group = "prod-web",
                    Program = "nginx",
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
            return String.Equals(request.Headers["Authorization"], "Bearer " + ApiToken, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        protected override object UnauthorizedBody()
        {
            return Error("Access is unauthorized.", "Unauthorized");
        }

        /// <inheritdoc />
        protected override StubResponse Handle(string path, NameValueCollection query)
        {
            if (path == "/v1/logs") return Logs(query);
            if (path == "/v1/entities") return Entities(query);
            if (path.StartsWith("/v1/entities/", StringComparison.Ordinal)) return Entity(Uri.UnescapeDataString(path.Substring("/v1/entities/".Length)));
            if (path.StartsWith("/v1/metrics/", StringComparison.Ordinal) && path.EndsWith("/measurements", StringComparison.Ordinal))
            {
                string name = Uri.UnescapeDataString(path.Substring("/v1/metrics/".Length, path.Length - "/v1/metrics/".Length - "/measurements".Length));
                return Measurements(name, query);
            }
            return new StubResponse(404, Error("Not found.", "ResourceNotFound"));
        }

        #endregion

        #region Private-Methods

        private StubResponse Logs(NameValueCollection query)
        {
            DateTime? start = null;
            DateTime? end = null;
            if (!TryTime(query["startTime"], out start)) return new StubResponse(400, Error("Invalid startTime.", "InvalidRequest", "startTime"));
            if (!TryTime(query["endTime"], out end)) return new StubResponse(400, Error("Invalid endTime.", "InvalidRequest", "endTime"));
            int pageSize = (int)Math.Clamp(ParseLong(query["pageSize"]) ?? 50, 1, 1000);
            int offset = 0;
            if (!String.IsNullOrEmpty(query["skipToken"]) && !TryDecodeOffset(query["skipToken"]!, out offset))
                return new StubResponse(400, Error("Invalid skipToken.", "InvalidRequest", "skipToken"));

            string? filter = query["filter"];
            string? term = String.IsNullOrWhiteSpace(filter) ? null : filter.Trim().Trim('"');
            string? group = query["group"];
            string? entityId = query["entityId"];
            string direction = String.IsNullOrEmpty(query["direction"]) ? "backward" : query["direction"]!;

            IEnumerable<StubLogEvent> matches = Events.Where(e =>
                (term == null || e.Message.Contains(term, StringComparison.OrdinalIgnoreCase))
                && (String.IsNullOrEmpty(group) || e.Group == group)
                && (String.IsNullOrEmpty(entityId) || e.EntityId == entityId)
                && (!start.HasValue || e.Time >= start.Value)
                && (!end.HasValue || e.Time <= end.Value));
            List<StubLogEvent> ordered = direction == "backward" ? matches.OrderByDescending(e => e.Time).ToList() : matches.OrderBy(e => e.Time).ToList();
            List<StubLogEvent> page = ordered.Skip(offset).Take(pageSize).ToList();
            string next = offset + pageSize < ordered.Count
                ? "/v1/logs?skipToken=" + Uri.EscapeDataString(EncodeOffset(offset + pageSize)) + "&pageSize=" + pageSize.ToString(CultureInfo.InvariantCulture)
                : String.Empty;

            Dictionary<string, object?> body = new Dictionary<string, object?>
            {
                { "logs", page.Select(e => new Dictionary<string, object?>
                    {
                        { "id", e.Id.ToString(CultureInfo.InvariantCulture) },
                        { "time", Iso(e.Time) },
                        { "message", e.Message },
                        { "hostname", e.Host },
                        { "severity", e.Severity.ToUpperInvariant() },
                        { "program", e.Program }
                    }).ToList() },
                { "pageInfo", new Dictionary<string, string> { { "prevPage", offset > 0 ? "prev" : String.Empty }, { "nextPage", next } } }
            };
            return new StubResponse(200, body);
        }

        private static List<Dictionary<string, object?>> AllEntities()
        {
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            string[] names = new string[] { "web-01", "web-02", "db-01" };
            for (int i = 0; i < names.Length; i++)
            {
                list.Add(EntityJson("e-100" + (i + 1), "Host", names[i], i == 2 ? "MODERATE" : "GOOD", i == 1,
                    new Dictionary<string, string> { { "env", "prod" }, { "role", i == 2 ? "db" : "web" } }));
            }
            list.Add(EntityJson("e-2001", "Service", "checkout", "BAD", false, new Dictionary<string, string> { { "env", "prod" } }));
            return list;
        }

        private static Dictionary<string, object?> EntityJson(string id, string type, string name, string health, bool maintenance, Dictionary<string, string> tags)
        {
            return new Dictionary<string, object?>
            {
                { "id", id },
                { "type", type },
                { "name", name },
                { "displayName", name },
                { "createdTime", "2026-01-01T00:00:00Z" },
                { "updatedTime", "2026-10-01T00:00:00Z" },
                { "lastSeenTime", Iso(DateTime.UtcNow.AddSeconds(-30)) },
                { "inMaintenance", maintenance },
                { "healthscore", new Dictionary<string, object> { { "score", health == "GOOD" ? 95 : 40 }, { "category", health == "GOOD" ? "good" : "moderate" } } },
                { "healthState", new Dictionary<string, string> { { "state", health } } },
                { "tags", tags },
                { "attributes", new Dictionary<string, object> { { "osName", "Ubuntu" }, { "cpuCount", 4 }, { "nested", new Dictionary<string, object> { { "a", 1 } } } } }
            };
        }

        private StubResponse Entities(NameValueCollection query)
        {
            string? type = query["type"];
            string? name = query["name"];
            int pageSize = (int)Math.Clamp(ParseLong(query["pageSize"]) ?? 50, 1, 1000);
            int offset = 0;
            if (!String.IsNullOrEmpty(query["skipToken"]) && !TryDecodeOffset(query["skipToken"]!, out offset))
                return new StubResponse(400, Error("Invalid skipToken.", "InvalidRequest", "skipToken"));

            List<Dictionary<string, object?>> matches = AllEntities()
                .Where(e => (String.IsNullOrEmpty(type) || String.Equals((string?)e["type"], type, StringComparison.OrdinalIgnoreCase))
                    && (String.IsNullOrEmpty(name) || ((string?)e["name"] ?? String.Empty).Contains(name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            List<Dictionary<string, object?>> page = matches.Skip(offset).Take(pageSize).ToList();
            string next = offset + pageSize < matches.Count ? EncodeOffset(offset + pageSize) : String.Empty;
            return new StubResponse(200, new Dictionary<string, object?>
            {
                { "entities", page },
                { "pageInfo", new Dictionary<string, string> { { "prevPage", String.Empty }, { "nextPage", next } } }
            });
        }

        private static StubResponse Entity(string id)
        {
            Dictionary<string, object?>? entity = AllEntities().FirstOrDefault(e => (string?)e["id"] == id);
            if (entity == null) return new StubResponse(404, Error("The server cannot find the requested resource.", "ResourceNotFound"));
            return new StubResponse(200, entity);
        }

        private StubResponse Measurements(string name, NameValueCollection query)
        {
            if (!String.Equals(name, KnownMetric, StringComparison.Ordinal)) return new StubResponse(404, Error("Metric not found.", "ResourceNotFound"));
            if (String.IsNullOrEmpty(query["seriesType"])) return new StubResponse(400, Error("seriesType is required.", "InvalidRequest", "seriesType"));
            DateTime? start;
            DateTime? end;
            if (!TryTime(query["startTime"], out start) || !TryTime(query["endTime"], out end)) return new StubResponse(400, Error("Invalid time.", "InvalidRequest", "startTime"));
            DateTime s = start ?? DateTime.UtcNow.AddHours(-1);
            DateTime e = end ?? DateTime.UtcNow;

            List<string> ids = new List<string> { "e-1001", "e-1002", "e-1003" };
            string? filter = query["filter"];
            if (!String.IsNullOrEmpty(filter) && filter.StartsWith("id:[", StringComparison.Ordinal) && filter.EndsWith("]", StringComparison.Ordinal))
                ids = filter.Substring(4, filter.Length - 5).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            int bucket = (int)Math.Max(60, Math.Ceiling((e - s).TotalSeconds / 120));
            int pageSize = (int)Math.Clamp(ParseLong(query["pageSize"]) ?? 20, 1, 1000);
            int offset = 0;
            if (!String.IsNullOrEmpty(query["skipToken"]) && !TryDecodeOffset(query["skipToken"]!, out offset))
                return new StubResponse(400, Error("Invalid skipToken.", "InvalidRequest", "skipToken"));

            List<Dictionary<string, object?>> groupings = new List<Dictionary<string, object?>>();
            for (int g = 0; g < ids.Count; g++)
            {
                List<Dictionary<string, object?>> points = new List<Dictionary<string, object?>>();
                int index = 0;
                for (DateTime t = s; t <= e; t = t.AddSeconds(bucket), index++)
                    points.Add(new Dictionary<string, object?> { { "time", Iso(t) }, { "value", index == 2 ? null : (object)(10.0 + (index % 7) * 5 + g) } });
                groupings.Add(new Dictionary<string, object?>
                {
                    { "attributes", new List<Dictionary<string, string>> { new Dictionary<string, string> { { "key", "id" }, { "value", ids[g] } } } },
                    { "measurements", points }
                });
            }

            List<Dictionary<string, object?>> page = groupings.Skip(offset).Take(pageSize).ToList();
            string next = offset + pageSize < groupings.Count ? EncodeOffset(offset + pageSize) : String.Empty;
            return new StubResponse(200, new Dictionary<string, object?>
            {
                { "groupings", page },
                { "bucketSizeInSeconds", bucket },
                { "pageInfo", new Dictionary<string, string> { { "prevPage", String.Empty }, { "nextPage", next } } }
            });
        }

        private static Dictionary<string, object?> Error(string message, string code, string? target = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "message", message }, { "code", code } };
            if (target != null) body["target"] = target;
            return body;
        }

        private static bool TryTime(string? value, out DateTime? time)
        {
            time = null;
            if (String.IsNullOrEmpty(value)) return true;
            if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                return false;
            time = parsed.UtcDateTime;
            return true;
        }

        private static string Iso(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static string EncodeOffset(int offset)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"offset\":" + offset.ToString(CultureInfo.InvariantCulture) + "}"));
        }

        private static bool TryDecodeOffset(string token, out int offset)
        {
            offset = 0;
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                if (!text.StartsWith("{\"offset\":", StringComparison.Ordinal)) return false;
                return Int32.TryParse(text.Substring(10).TrimEnd('}'), NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static long? ParseLong(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            return Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;
        }

        #endregion
    }
}
