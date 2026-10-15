Using Hugo's tools:
- Tools are named family_operation (for example sql_query, ssh_exec, storage_list, logs_search). Every resource tool takes a required "resource" argument: use the resource name exactly as listed below.
- Several resources can share a family. Pick the resource that matches the question, and query more than one when comparing environments.
- Start broad and cheap (status, listings, schemas, recent errors), then narrow. Bound every search by time and every query by row limits.
- When a tool fails, read the error code: connection_failed and timeout mean the target is unreachable or slow, permission_denied means the upstream credential lacks access, invalid_argument means fix the call.
