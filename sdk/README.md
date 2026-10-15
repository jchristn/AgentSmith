# Hugo SDKs

| SDK | Directory | Package | Harness |
|---|---|---|---|
| C# | [`csharp`](csharp/README.md) | `Hugo.Sdk` (net8.0, net10.0) | `csharp/src/Test.Automated` (console) |
| Python | [`python`](python/README.md) | `hugo-sdk` (sync and async, httpx) | `python/tests` (pytest) and `python/run_harness.py` |
| JavaScript and TypeScript | [`js`](js/README.md) | `@jchristn/hugo-sdk` (ESM and CJS, fetch) | `js/test` (Vitest) |

All three cover the same surface with the same names in each language's casing, carry the product version `0.1.0-alpha`, and default to `http://127.0.0.1:8800`. Everything, including conversations, chat streaming, `ask`, and analytics, is verified against a live server.

Every harness provisions its own tenant with the system administrator account (`HUGO_SDK_ADMIN_EMAIL`, `HUGO_SDK_ADMIN_PASSWORD`, `HUGO_SDK_ADMIN_TENANT`) on the server named by `HUGO_SDK_SERVER`, and deletes it at the end. Credentials the harnesses create get an explicit role, because credentials do not inherit their owner's administrator bypass. Set `HUGO_SDK_ADMIN_API_KEY` to the server's `HUGO_ADMIN_API_KEY` to run the administrator API key tests.

The harnesses run against a server with default settings. They need no external model: each starts a small OpenAI-compatible stub model on a free 127.0.0.1 port and points a model endpoint at it for the chat, `ask`, and analytics tests. Token routes are rate limited (`Auth.RateLimitPerMinute`, 10 per minute per IP), so the harness clients turn on the SDKs' opt-in 429 retry (C# `RateLimitRetries`, Python `rate_limit_retries`, JavaScript `rateLimitRetries`; default 0, off), which waits for `Retry-After` and tries again. A full run therefore pauses for a minute or two.

`mustChangePassword` is advisory (D-048): every route works while it is set, and no harness changes a password to clear it.

Scripts:

- `scripts/run-harnesses.sh` runs the three harnesses and the coverage check against a running server.
- `scripts/check_coverage.py` compares the operations in the server's `/openapi.json` with each SDK's source and lists gaps.
