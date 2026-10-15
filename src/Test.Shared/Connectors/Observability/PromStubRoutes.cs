namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Recorded Prometheus HTTP API response shapes for <see cref="ObservabilityStub"/>.
    /// </summary>
    public class PromStubRoutes
    {
        #region Public-Members

        /// <summary>
        /// Series returned by query, query_range, and series. Default 3.
        /// </summary>
        public int SeriesCount { get; set; } = 3;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle a request.
        /// </summary>
        /// <param name="r">Request.</param>
        /// <returns>Reply.</returns>
        public StubReply Handle(StubRequest r)
        {
            switch (r.Path)
            {
                case "/api/v1/status/buildinfo":
                    return StubReply.Json("{\"status\":\"success\",\"data\":{\"version\":\"3.7.0\",\"revision\":\"abc\",\"branch\":\"HEAD\",\"goVersion\":\"go1.25\"}}");
                case "/api/v1/query":
                    return Query(r);
                case "/api/v1/query_range":
                    return QueryRange(r);
                case "/api/v1/alerts":
                    return StubReply.Json("{\"status\":\"success\",\"data\":{\"alerts\":["
                        + "{\"labels\":{\"alertname\":\"HighErrorRate\",\"severity\":\"page\",\"job\":\"api\"},\"annotations\":{\"summary\":\"API 5xx above 5%\"},\"state\":\"firing\",\"activeAt\":\"2026-10-14T07:55:06.7Z\",\"value\":\"7.5e-02\"},"
                        + "{\"labels\":{\"alertname\":\"DiskFilling\",\"severity\":\"warn\"},\"annotations\":{\"description\":\"disk 85%\"},\"state\":\"pending\",\"activeAt\":\"2026-10-14T08:01:00Z\",\"value\":\"8.5e+01\"}"
                        + "]}}");
                case "/api/v1/rules":
                    return StubReply.Json("{\"status\":\"success\",\"data\":{\"groups\":[{\"name\":\"api\",\"file\":\"/etc/prometheus/api.yml\",\"rules\":["
                        + "{\"state\":\"firing\",\"name\":\"HighErrorRate\",\"query\":\"sum(rate(http_requests_total{code=~\\\"5..\\\"}[5m])) / sum(rate(http_requests_total[5m])) > 0.05\",\"duration\":300,\"labels\":{\"severity\":\"page\"},\"alerts\":[{\"labels\":{\"alertname\":\"HighErrorRate\"},\"state\":\"firing\",\"value\":\"0.075\"}],\"health\":\"ok\",\"lastEvaluation\":\"2026-10-14T08:00:00Z\",\"type\":\"alerting\"},"
                        + "{\"name\":\"job:requests:rate5m\",\"query\":\"sum by (job) (rate(http_requests_total[5m]))\",\"health\":\"ok\",\"type\":\"recording\"}"
                        + "],\"interval\":15},{\"name\":\"broken\",\"file\":\"/etc/prometheus/broken.yml\",\"rules\":["
                        + "{\"name\":\"broken:rule\",\"query\":\"sum(nope\",\"health\":\"err\",\"lastError\":\"parse error\",\"type\":\"recording\"}"
                        + "],\"interval\":60}]}}");
                case "/api/v1/targets":
                    return StubReply.Json("{\"status\":\"success\",\"data\":{\"activeTargets\":["
                        + "{\"discoveredLabels\":{\"__address__\":\"10.0.0.1:9090\",\"job\":\"api\"},\"labels\":{\"instance\":\"10.0.0.1:9090\",\"job\":\"api\"},\"scrapePool\":\"api\",\"scrapeUrl\":\"http://10.0.0.1:9090/metrics\",\"lastError\":\"\",\"lastScrape\":\"2026-10-14T08:00:00Z\",\"lastScrapeDuration\":0.0026,\"health\":\"up\"},"
                        + "{\"discoveredLabels\":{\"__address__\":\"10.0.0.2:9100\",\"job\":\"node\"},\"labels\":{\"instance\":\"10.0.0.2:9100\",\"job\":\"node\"},\"scrapePool\":\"node\",\"scrapeUrl\":\"http://10.0.0.2:9100/metrics\",\"lastError\":\"connection refused\",\"lastScrape\":\"2026-10-14T08:00:01Z\",\"lastScrapeDuration\":0.0002,\"health\":\"down\"}"
                        + "],\"droppedTargets\":[{\"discoveredLabels\":{\"__address__\":\"10.0.0.3:80\",\"job\":\"dropped-job\"}}]}}");
                case "/api/v1/series":
                    {
                        StringBuilder sb = new StringBuilder("{\"status\":\"success\",\"data\":[");
                        for (int i = 0; i < SeriesCount; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append("{\"__name__\":\"up\",\"job\":\"api\",\"instance\":\"i-").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\"}");
                        }
                        sb.Append("]}");
                        return StubReply.Json(sb.ToString());
                    }
                case "/api/v1/labels":
                    return StubReply.Json("{\"status\":\"success\",\"data\":[\"__name__\",\"instance\",\"job\"]}");
                case "/api/v1/label/job/values":
                    return StubReply.Json("{\"status\":\"success\",\"data\":[\"api\",\"node\",\"worker\"]}");
                default:
                    return StubReply.Status(404, "404 page not found");
            }
        }

        #endregion

        #region Private-Methods

        private StubReply Query(StubRequest r)
        {
            string q = r.Query["query"] ?? String.Empty;
            if (q.Contains("bad(", StringComparison.Ordinal))
                return StubReply.Status(400, "{\"status\":\"error\",\"errorType\":\"bad_data\",\"error\":\"1:4: parse error: unclosed left parenthesis\"}");
            double t = Double.Parse(r.Query["time"] ?? "0", CultureInfo.InvariantCulture);
            string ts = t.ToString("0.###", CultureInfo.InvariantCulture);
            if (q == "scalar(1)") return StubReply.Json("{\"status\":\"success\",\"data\":{\"resultType\":\"scalar\",\"result\":[" + ts + ",\"1\"]}}");
            StringBuilder sb = new StringBuilder("{\"status\":\"success\",\"data\":{\"resultType\":\"vector\",\"result\":[");
            for (int i = 0; i < SeriesCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"metric\":{\"__name__\":\"http_requests_total\",\"job\":\"api\",\"instance\":\"i-").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\"},\"value\":[").Append(ts).Append(",\"").Append((i * 10 + 1).ToString(CultureInfo.InvariantCulture)).Append("\"]}");
            }
            sb.Append("]},\"warnings\":[\"stub warning\"]}");
            return StubReply.Json(sb.ToString());
        }

        private StubReply QueryRange(StubRequest r)
        {
            double start = Double.Parse(r.Query["start"] ?? "0", CultureInfo.InvariantCulture);
            double end = Double.Parse(r.Query["end"] ?? "0", CultureInfo.InvariantCulture);
            double step = Double.Parse(r.Query["step"] ?? "60", CultureInfo.InvariantCulture);
            StringBuilder sb = new StringBuilder("{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[");
            for (int i = 0; i < SeriesCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"metric\":{\"job\":\"api\",\"instance\":\"i-").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\"},\"values\":[");
                int n = 0;
                for (double t = start; t <= end && n < 20000; t += step, n++)
                {
                    if (n > 0) sb.Append(',');
                    sb.Append('[').Append(t.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\"").Append((n % 10).ToString(CultureInfo.InvariantCulture)).Append("\"]");
                }
                sb.Append("]}");
            }
            sb.Append("]}}");
            return StubReply.Json(sb.ToString());
        }

        #endregion
    }
}
