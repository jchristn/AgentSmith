# Logging connector API notes

These notes record what was verified about the SolarWinds Observability (SWO) public API on 2026-10-14 for task P4.24. The `solarwinds-observability` connector in `src/Hugo.Connectors.Logging` and its stub in `src/Test.Shared/Connectors/Logging` follow these shapes. Where the documentation is silent or ambiguous, the note describes how the connector handles it.

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
