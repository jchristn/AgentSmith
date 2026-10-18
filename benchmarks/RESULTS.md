# Prompt document measurements

Development round, 2026-10-18. These measurements are indicative: other feature
builds and tests ran on the same workstation. They establish fixture correctness
and a local baseline, not production latency promises.

The system under test was the working-tree .NET 10 server on
`http://127.0.0.1:8894`, with a separate temporary SQLite database. Hardware:
Apple M5 Max, 18 logical processors, 128 GiB RAM, macOS 26.6, ARM64. The public
REST harness used a single pool, four connections maximum, 60-second timeout and
30 measured samples for each cold/warm/read operation. No real model was invoked.
Cold composition means a new immutable description revision invalidated the
composition immediately before measurement; warm means an unchanged repeat.
The empty-profile baseline has no document invalidation and is labeled separately.

| Fixture | Warm composition p50 / p95 (ms) | Complete prompt bytes | Estimated tokens |
|---|---|---|---|
| No attachments | 0.671 / 3.549 | 6366 | 1592 |
| 1 Reference, 256-byte body | 0.799 / 1.919 | 7478 | 1870 |
| 1 Reference, 262080-byte body | 0.917 / 1.589 | 7483 | 1871 |
| 16 Reference, 256-byte bodies | 1.395 / 2.026 | 11941 | 2986 |
| 16 Reference, 262080-byte bodies | 1.929 / 2.479 | 12021 | 3006 |
| 32 Reference, 256-byte bodies | 2.129 / 2.732 | 16709 | 4178 |
| 32 Reference, 262080-byte bodies | 2.708 / 3.222 | 16869 | 4218 |
| 32 Inline, 256-byte bodies | 2.094 / 2.742 | 29447 | 7362 |

For 32 near-limit Reference documents, cold composition was 3.529 / 4.139 ms
(p50/p95). A complete management read of a 262080-byte revision was
1.489 / 1.748 ms. This measures the explicit management content endpoint,
not model-facing MCP chunk retrieval.

Increasing 32 Reference bodies from 8192 total bytes to 8386560 total bytes
changed the complete compiled prompt by 160 bytes, attributable to bounded
metadata such as byte counts and filenames. No body marker appeared in Reference
prompts. All Inline prompts contained complete selected bodies, and every
measured revision read matched canonical content, byte count and SHA-256.
All ten scenarios completed, each with 30 composition sample pairs. Raw reports
retain the individual samples under ignored `benchmarks/results/`.

The first diagnostic run found repeated per-document tool instructions making
32 short references exceed the default manifest budget. Moving shared retrieval
guidance into the catalog wrapper fixed that case. A second finding was metadata
paths loading complete revision bodies. Explicit metadata-only reads and paged
history queries removed those unnecessary body allocations. The paired local
comparison reported zero regressions at the preset 1.20 median ratio, greater
than 1 ms absolute increase and fixed-seed bootstrap interval threshold. These
runs were not isolated enough to attribute small latency changes confidently.

Reproduce with the commands in `README.md`, retaining source and server build
identifiers in each report. Before using latency numbers as a release gate, rerun
on an otherwise idle workstation against the final committed build and preserve
both raw reports. No failed or partially ingested run is comparable.
