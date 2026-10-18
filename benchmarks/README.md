# Prompt document benchmark

The focused harness in `src/Test.Benchmark/` measures public REST composition and
complete management revision reads. It does not call product assemblies or query
application tables. It uses one HTTP pool, four connections per server and a
60-second timeout. The harness is registered in the solution and builds for the
same .NET targets as Hugo.

Use a separate Hugo instance with its own database and a tool-capable endpoint if
model capability validation requires one. The default target is
`http://127.0.0.1:8894`, which is separate from the normal Compose listener. Obtain
an authorized session token for that isolated tenant and set
`HUGO_BENCHMARK_TOKEN` in the shell. Credentials are never command arguments or
report fields. The harness creates synthetic profiles and documents, then deletes
only the objects it created. A duplicate fixture name fails instead of modifying
an existing object. Do not run against a production tenant.

```bash
dotnet run --project src/Test.Benchmark -f net10.0 -- validate
dotnet run --project src/Test.Benchmark -f net10.0 -- documents \
  --isolated --provider sqlite --server-revision YOUR_SOURCE_AND_BUILD_ID \
  --url http://127.0.0.1:8894 --samples 30 --run-id apc-baseline \
  --output benchmarks/results/baseline.json
dotnet run --project src/Test.Benchmark -f net10.0 -- compare \
  --baseline benchmarks/results/baseline.json \
  --candidate benchmarks/results/candidate.json --max-ratio 1.20
```

An existing endpoint can be selected with `--endpoint-id`. All paths resolve from
the repository root even when the command is launched from another directory.
Use `--provider postgresql` when the isolated server uses PostgreSQL; this records
the provider and does not reconfigure the server.

The fixture covers a zero-document baseline, 1/16/32 attachments, Reference
bodies of exactly 256 and 262080 canonical UTF-8 bytes, and Inline bodies of 256
bytes. Every sample invalidates composition by creating a new description
revision, measures the next composition, then measures an unchanged composition.
The zero-document baseline is labeled separately because it cannot perform that
revision invalidation. Revision reads verify the entire body, UTF-8 size and
SHA-256 digest. Reference checks reject body markers in the prompt and confirm
that increasing body size changes prompt bytes only by bounded metadata.
Inline checks require the complete body. Historical bodies count toward tenant
storage quotas, and fixtures are cleaned between cases.

Reports include raw samples, p50/p95/mean/max latency, successful counts, prompt
bytes, estimated tokens, fixture sizes, source/server identifiers, OS,
architecture and CPU count. An API, correctness or cleanup failure marks the run
incomplete and exits 3. Ctrl+C exits 130 after attempting cleanup. Failed calls
never count as successful samples. A malformed command exits 2.

The comparison pairs corresponding samples, uses a fixed-seed 2000-resample
bootstrap of mean latency differences, and flags a regression only when the 95%
interval excludes zero, median latency exceeds 1.20 times baseline, and the
absolute median increase exceeds 1 ms. Select a threshold before the candidate
run and keep it unchanged. Compare the same machine, provider, fixture and sample
count. Inspect raw samples and error counts. Runs made while builds or other
benchmarks compete for the machine are indicative diagnostics.

This command is a focused feature benchmark. It does not claim a platform-wide
load, model-quality, concurrent-arrival, MCP chunk retrieval or resilience result.
Those behaviors remain covered by integration tests and separate operational
measurements. The supplied fixture Markdown is synthetic. Raw local results are
ignored by Git; curated, sanitized findings belong in `RESULTS.md`.
