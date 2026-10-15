# Logging connector API notes

These notes record what was verified about the Papertrail and SolarWinds Observability (SWO) public APIs on 2026-10-14 for plan tasks P4.20 and P4.24. The `papertrail` and `solarwinds-observability` connectors in `src/Hugo.Connectors.Logging` and the stubs in `src/Test.Shared/Connectors/Logging` follow these shapes. Where the documentation is silent or ambiguous, the note says so and describes what the connector does about it.

## Papertrail

Sources:

- HTTP API overview: https://www.papertrail.com/help/http-api/
- Search API: https://www.papertrail.com/help/search-api/
- Settings API (systems, groups, saved searches): https://www.papertrail.com/help/settings-api/

### Base URL and authentication

- Base URL: `https://papertrailapp.com/api/v1/`.
- Authentication: header `X-Papertrail-Token: <token>`. HTTP Basic with the account username and password also works, but Hugo only uses the token.
- Responses are JSON. Lists are arrays and single items are objects.

### Errors and rate limits

| Status | Meaning |
|---|---|
| 400 | Bad request. Body is `{"message": "..."}`. |
| 401 | Authentication failed. |
| 404 | Unknown resource. |
| 405 | Unsupported method. |
| 429 | Rate limit exceeded ("429 Rate Limit Exceeded"). |

Rate limit headers on every response:

| Header | Meaning |
|---|---|
| `X-Rate-Limit-Limit` | Requests allowed per window. Documented as currently 25. |
| `X-Rate-Limit-Remaining` | Requests left in the current window. |
| `X-Rate-Limit-Reset` | Seconds until the window resets. Documented as currently 5. |

The documentation says the numbers may change, so clients should read the headers. It does not say whether a 429 carries `Retry-After`. The connector reports `Retry-After` when present and falls back to `X-Rate-Limit-Reset`.

### Search: `GET /events/search.json`

| Parameter | Meaning |
|---|---|
| `q` | Search query, same syntax as the web UI. URL-encoded. Omit for all events. |
| `system_id` | Limit to one system. Accepts a numeric ID or a unique name made of letters, numbers, and underscores. |
| `group_id` | Limit to one group. Numeric ID only (the connector resolves a group name to its ID through `groups.json`). |
| `min_id` | Return events newer than this event ID. |
| `max_id` | Return events older than this event ID. |
| `min_time` | Oldest timestamp to examine, epoch seconds UTC. |
| `max_time` | Newest timestamp to examine, epoch seconds UTC. |
| `limit` | Maximum events returned. Default 1000, maximum 10000. |
| `tail` | Defaults to true when `min_id` is given without `max_id` or `max_time`. Favors recency over completeness. `tail=false` favors completeness. |

Response:

```json
{
  "min_id": "7711561783320576",
  "max_id": "7711582041804800",
  "min_time_at": "2026-10-14T10:00:01-07:00",
  "max_time_at": "2026-10-14T10:59:58-07:00",
  "reached_beginning": false,
  "reached_record_limit": true,
  "reached_time_limit": false,
  "events": [
    {
      "id": "7711561783320576",
      "generated_at": "2026-10-14T10:00:01-07:00",
      "received_at": "2026-10-14T10:00:01-07:00",
      "display_received_at": "Oct 14 10:00:01",
      "source_name": "web-01",
      "source_id": 5381,
      "hostname": "web-01",
      "source_ip": "10.0.0.11",
      "facility": "Local7",
      "severity": "Info",
      "program": "nginx",
      "message": "GET /health 200"
    }
  ]
}
```

- `id` is a 64-bit integer serialized as a string. IDs increase with time but are not sequential.
- `received_at` uses the API token owner's time zone. `program` may be null and `message` may be empty.
- `reached_end` appears on forward searches. `min_time_at` and `max_time_at` are optional.
- Each request has a server-side time budget of about 5 seconds; hitting it sets `reached_time_limit`.

Paging:

- Live tail: first request without IDs, then pass the previous `max_id` as `min_id`.
- Scroll back: pass the previous `min_id` as `max_id`.
- Time range: send `min_time` and `max_time`. While the result is incomplete (`reached_record_limit` or `reached_time_limit`), move back by passing `min_id` as `max_id`, keeping `min_time`.

What the connector does: `logs_search` sends `min_time`, `max_time`, `limit`, and the filters, and returns an opaque cursor that carries `max_id` (the oldest returned event ID) and the original window. The cursor is null when neither `reached_record_limit` nor `reached_time_limit` is set, or when `reached_beginning` is true. `logs_tail` sends no time range, and its cursor carries the newest ID as `min_id`. `logs_volume` pages backward the same way, up to `maxScanEvents`, and buckets by `received_at`.

Ambiguity: the docs do not say whether an event exactly at `min_id`/`max_id` is included. The connector treats both bounds as exclusive, which matches "newer than" and "older than" in the docs.

### Systems: `GET /systems.json`, `GET /systems/<id>.json`

```json
[
  {
    "id": 3248,
    "name": "web-01",
    "last_event_at": "2026-10-14T10:59:58-07:00",
    "ip_address": "10.0.0.11",
    "hostname": null,
    "syslog": { "hostname": "logs.papertrailapp.com", "port": 514 },
    "_links": { "self": { "href": "..." }, "search": { "href": "..." }, "html": { "href": "..." } }
  }
]
```

No paging. The list is returned whole.

### Groups: `GET /groups.json`, `GET /groups/<id>.json`

```json
[
  {
    "id": 31,
    "name": "All Systems",
    "system_wildcard": "*",
    "systems": [ { "id": 3248, "name": "web-01" } ],
    "_links": { "self": { "href": "..." }, "search": { "href": "..." }, "html": { "href": "..." } }
  }
]
```

The docs show `systems` as an array of system objects (same fields as the systems list). The connector reads only `id` and `name` from each.

### Saved searches: `GET /searches.json`

```json
[
  {
    "id": 1111,
    "name": "Login rejections",
    "query": "\"access denied\" OR ssh",
    "group": { "id": 31, "name": "All Systems", "_links": { "self": { "href": "..." } } },
    "_links": { "self": { "href": "..." }, "search": { "href": "..." }, "html": { "href": "..." } }
  }
]
```

## SolarWinds Observability

Sources:

- REST API overview: https://documentation.solarwinds.com/en/success_center/observability/content/api/api-swagger.htm
- Data centers and endpoints: https://documentation.solarwinds.com/en/success_center/observability/content/system_requirements/endpoints.htm
- OpenAPI document (version 1.0.19 at the time of writing): https://api.na-01.cloud.solarwinds.com/v1/openapi.json
- Swagger UI: https://api.na-01.cloud.solarwinds.com/v1/#/

### Base URL and authentication

| Data center | Public API host |
|---|---|
| `na-01` (North America, AWS) | `https://api.na-01.cloud.solarwinds.com` |
| `na-02` (North America, Azure) | `https://api.na-02.cloud.solarwinds.com` |
| `eu-01` (Europe, Frankfurt, AWS) | `https://api.eu-01.cloud.solarwinds.com` |
| `ap-01` (Australia, AWS) | `https://api.ap-01.cloud.solarwinds.com` |

- Paths start with `/v1/`. The connector's base URL is `https://api.<dataCenter>.cloud.solarwinds.com/v1`, or the `baseUrl` setting when set.
- Authentication: `Authorization: Bearer <token>`. The documentation says a full-access API token is required for the public API. Ingestion tokens are for the collectors and do not work here.
- Log ingestion uses a different host (`logs.collector.<dc>.cloud.solarwinds.com`). Hugo never ingests.

### Errors and rate limits

Error bodies are `{"message": "...", "code": "Unauthorized" | "AccessForbidden" | "InvalidRequest" | "ResourceNotFound" | "InternalError" | "ServiceUnavailable" | "ResourceConflict", "target": "..."}` (`target` only on 400 and 409). The OpenAPI document lists 400, 401, 403, 404, 409, 500, and 503 across operations.

Not documented: rate limits, rate limit headers, and 429. The connector treats 429 as `rate_limited` and reports `Retry-After` if the response carries it.

### Logs search: `GET /v1/logs` (`searchLogs`)

| Parameter | Type | Meaning |
|---|---|---|
| `filter` | string | Search query string. |
| `group` | string | Filter by log group name. |
| `entityId` | string | Filter by entity ID. |
| `startTime` | date-time | ISO 8601 UTC, `yyyy-MM-ddTHH:mm:ssZ`. |
| `endTime` | date-time | ISO 8601 UTC, `yyyy-MM-ddTHH:mm:ssZ`. |
| `direction` | string | `backward` (default), `forward`, or `tail`. |
| `pageSize` | int32 | Items per page. "Default varies by API." |
| `skipToken` | string | Token for the requested page. |

Response:

```json
{
  "logs": [
    {
      "id": "1234567890123456789",
      "time": "2026-10-14T17:00:01Z",
      "message": "GET /health 200",
      "hostname": "web-01",
      "severity": "INFO",
      "program": "nginx"
    }
  ],
  "pageInfo": { "prevPage": "", "nextPage": "eyJvZmZzZXQiOjUwfQ" }
}
```

Ambiguities:

- The `direction` description says "Backward sorts logs from oldest to newest, forward sorts logs from newest to oldest, and tail sorts from oldest to newest". The wording is contradictory with the names. The connector passes `direction` through from `providerOptions.direction` and defaults to the API default (`backward`). `logs_search` keeps the order SWO returns; `logs_tail` sorts its page by time, oldest first, so the newest event is last.
- Tail paging has no documented "newer than" token. `logs_tail` stores the time and ID of the newest event it returned, sends that time as `startTime` on the next call, and drops events at or before it (by time, then ID).
- `pageInfo.nextPage` is described as a token, but community reports show it as a URL or path with a `skipToken` query parameter. The connector accepts both: when the value contains `skipToken=`, it extracts the parameter; otherwise it uses the value as the token.
- The maximum `pageSize` is not documented. The connector caps it with the resource's `maxEvents`.
- A community report says `skipToken` paging is tied to the original query. The connector stores the resolved window, filter, group, entity, and direction in its cursor and replays them with the token.

### Entities

`GET /v1/entities` (`listEntities`): `type` (entity type; empty means all), `name` (case-insensitive contains), `search` (contextual query; combined with `name` by AND), `pageSize`, `skipToken`.

```json
{
  "entities": [
    {
      "id": "e-1234567890",
      "type": "Host",
      "name": "web-01",
      "displayName": "web-01",
      "createdTime": "2026-01-01T00:00:00Z",
      "updatedTime": "2026-10-14T00:00:00Z",
      "lastSeenTime": "2026-10-14T17:00:00Z",
      "inMaintenance": false,
      "healthState": { "state": "GOOD" },
      "tags": { "env": "prod" },
      "attributes": { "osName": "Ubuntu" }
    }
  ],
  "pageInfo": { "prevPage": "", "nextPage": "" }
}
```

`GET /v1/entities/{id}` (`getEntityById`) returns one entity object with the same fields, or 404. `healthscore` (`{score, category}`) is deprecated in favor of `healthState.state` (`GOOD`, `MODERATE`, `AT_RISK`, `DEGRADED`, `BAD`, `UNKNOWN`, `DISABLED`). `GET /v1/metadata/entities/types` returns `{"types": ["..."]}`; the connector does not expose it as a tool yet.

### Metrics: `GET /v1/metrics/{name}/measurements` (`listMetricMeasurements`)

| Parameter | Meaning |
|---|---|
| `filter` | Filter on attributes, for example `id:[e-123,e-456] category:moderate`. |
| `groupBy` | Comma-delimited attribute names. |
| `aggregateBy` | `AVG` (default), `COUNT`, `MIN`, `MAX`, `SUM`, `LAST`. |
| `preGroupBy`, `preGroupByMethod` | Secondary aggregation inside buckets; set together. |
| `seriesType` | `TIMESERIES` (default) or `SCALAR`. Marked required in the OpenAPI document. |
| `startTime`, `endTime` | ISO 8601 UTC. |
| `pageSize`, `skipToken` | Paging over groupings. |

```json
{
  "groupings": [
    {
      "attributes": [ { "key": "id", "value": "e-1234567890" } ],
      "measurements": [ { "time": "2026-10-14T17:00:00Z", "value": 12.5 } ]
    }
  ],
  "bucketSizeInSeconds": 60,
  "pageInfo": { "prevPage": "", "nextPage": "" }
}
```

`value` is nullable. The filter syntax for entity scoping is only shown by example (`id: [id1,id2]`), so the connector builds `id:[<entityId>]` when `entityId` is given without an explicit `filter`.

### Alerts

The public REST API (OpenAPI 1.0.19) has no alert listing operation. The paths cover change events, cloud integrations, database observability, DEM probes and monitors, entities, integrations, logs, log archives, metadata, metrics, and ingestion tokens. `swo_list_alerts` is therefore omitted. Alerts may be reachable through the SWO GraphQL API, which is out of scope for wave 1.
