# Hugo SDKs

| SDK | Directory | Package | Harness |
|---|---|---|---|
| C# | [`csharp`](csharp/README.md) | `Hugo.Sdk` (net8.0, net10.0) | `csharp/src/Test.Automated` (console) |
| Python | [`python`](python/README.md) | `hugo-sdk` (sync and async, httpx) | `python/tests` (pytest) and `python/run_harness.py` |
| JavaScript and TypeScript | [`js`](js/README.md) | `@jchristn/hugo-sdk` (ESM and CJS, fetch) | `js/test` (Vitest) |

All three cover the same surface with the same names in each language's casing, carry the product version `0.1.0-alpha`, and default to `http://127.0.0.1:8800`. Conversations and chat streaming are a preview until the Phase 7 routes land; everything else is verified against a live server.

Every harness provisions its own tenant with the system administrator account (`HUGO_SDK_ADMIN_EMAIL`, `HUGO_SDK_ADMIN_PASSWORD`, `HUGO_SDK_ADMIN_TENANT`) on the server named by `HUGO_SDK_SERVER`, and deletes it at the end.

Scripts:

- `scripts/run-harnesses.sh` runs the three harnesses and the coverage check against a running server.
- `scripts/check_coverage.py` compares the operations in the server's `/openapi.json` with each SDK's source and lists gaps.
