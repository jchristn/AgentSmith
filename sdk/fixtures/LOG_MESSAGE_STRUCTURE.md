# Synthetic application log schema

Read this document before interpreting the example application's structured logs.

| Field | Type | Meaning |
| --- | --- | --- |
| timestamp | ISO 8601 string | Event time in UTC. |
| severity | string | `info`, `warning`, or `error`. |
| event | string | Stable event name, such as `request.completed`. |
| requestId | string | Correlates events belonging to one request. |
| durationMs | number | Elapsed request duration in milliseconds. |

A warning does not by itself mean a request failed. Check the event and its outcome.
This is synthetic example data, not a production schema.
