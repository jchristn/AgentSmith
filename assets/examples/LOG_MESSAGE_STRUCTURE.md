# Application log message structure

Consult this document before interpreting the synthetic application log format.

Each event is a JSON object:

```json
{"timestamp":"2026-01-01T00:00:00Z","severity":"Info","event":"operation.completed","operationId":"example-001","durationMs":12,"message":"Synthetic operation completed"}
```

| Field | Meaning |
|---|---|
| timestamp | UTC event time in ISO 8601 format |
| severity | Debug, Info, Warning, or Error |
| event | Stable event classification |
| operationId | Correlation identifier shared by related events |
| durationMs | Operation duration in milliseconds, when applicable |
| message | Human-readable summary |

Group related events by operationId. Use event and severity to classify failures.
Do not infer a user identity from operationId. Missing optional values mean unknown,
not zero. Examples are synthetic and contain no customer data or credentials.
