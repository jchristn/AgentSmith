namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Globalization;
    using System.Numerics;
    using System.Text;

    /// <summary>
    /// Recorded Loki HTTP API response shapes for <see cref="ObservabilityStub"/>. Log queries return two streams of
    /// lines inside the requested window; queries starting with sum, rate, or count_over_time return a matrix.
    /// </summary>
    public class LokiStubRoutes
    {
        #region Public-Members

        /// <summary>
        /// When set, requests without this X-Scope-OrgID get 401 like a multi-tenant Loki.
        /// </summary>
        public string? RequiredTenant { get; set; } = null;

        /// <summary>
        /// Lines per stream returned by log queries. Default 30.
        /// </summary>
        public int LinesPerStream { get; set; } = 30;

        /// <summary>
        /// When true, labels answer {"status":"success"} without a data field, like a Loki that has no lines yet.
        /// </summary>
        public bool Empty { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle a request.
        /// </summary>
        /// <param name="r">Request.</param>
        /// <returns>Reply.</returns>
        public StubReply Handle(StubRequest r)
        {
            if (RequiredTenant != null && r.Headers["X-Scope-OrgID"] != RequiredTenant)
                return new StubReply { StatusCode = 401, Body = "no org id", ContentType = "text/plain" };
            switch (r.Path)
            {
                case "/loki/api/v1/labels":
                    if (Empty) return StubReply.Json("{\"status\":\"success\"}");
                    return StubReply.Json("{\"status\":\"success\",\"data\":[\"app\",\"env\",\"level\"]}");
                case "/loki/api/v1/label/app/values":
                    return StubReply.Json("{\"status\":\"success\",\"data\":[\"hugo-worker\",\"hugo-api\"]}");
                case "/loki/api/v1/query_range":
                case "/loki/api/v1/query":
                    return Query(r);
                default:
                    return new StubReply { StatusCode = 404, Body = "404 page not found", ContentType = "text/plain" };
            }
        }

        #endregion

        #region Private-Methods

        private StubReply Query(StubRequest r)
        {
            string q = r.Query["query"] ?? String.Empty;
            if (q.Contains("|~ \"(\"", StringComparison.Ordinal))
                return new StubReply { StatusCode = 400, Body = "parse error at line 1, col 20: invalid regex", ContentType = "text/plain" };
            BigInteger end = BigInteger.Parse(r.Query["end"] ?? r.Query["time"] ?? "1760000000000000000", CultureInfo.InvariantCulture);
            if (q.StartsWith("sum", StringComparison.Ordinal) || q.StartsWith("rate", StringComparison.Ordinal) || q.StartsWith("count_over_time", StringComparison.Ordinal))
            {
                double endSeconds = (double)(end / 1000000000);
                string type = r.Path.EndsWith("query", StringComparison.Ordinal) ? "vector" : "matrix";
                if (type == "vector")
                    return StubReply.Json("{\"status\":\"success\",\"data\":{\"resultType\":\"vector\",\"result\":[{\"metric\":{\"level\":\"error\"},\"value\":[" + endSeconds.ToString("0", CultureInfo.InvariantCulture) + ",\"6\"]}]}}");
                return StubReply.Json("{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[{\"metric\":{\"level\":\"error\"},\"values\":[["
                    + (endSeconds - 60).ToString("0", CultureInfo.InvariantCulture) + ",\"2\"],[" + endSeconds.ToString("0", CultureInfo.InvariantCulture) + ",\"4\"]]}]}}");
            }
            StringBuilder sb = new StringBuilder("{\"status\":\"success\",\"data\":{\"resultType\":\"streams\",\"result\":[");
            AppendStream(sb, "hugo-api", end, 0);
            sb.Append(',');
            AppendStream(sb, "hugo-worker", end, 500000000);
            sb.Append("],\"stats\":{}}}");
            return StubReply.Json(sb.ToString());
        }

        private void AppendStream(StringBuilder sb, string app, BigInteger end, long offsetNs)
        {
            sb.Append("{\"stream\":{\"app\":\"").Append(app).Append("\",\"env\":\"testbed\"},\"values\":[");
            for (int i = 0; i < LinesPerStream; i++)
            {
                if (i > 0) sb.Append(',');
                BigInteger ts = end - new BigInteger((long)(i + 1) * 1000000000L + offsetNs);
                sb.Append("[\"").Append(ts.ToString(CultureInfo.InvariantCulture)).Append("\",\"level=").Append(i % 5 == 0 ? "error" : "info").Append(" app=").Append(app).Append(" msg=line ").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\"]");
            }
            sb.Append("]}");
        }

        #endregion
    }
}
