# Reviewed package versions

The direct .NET package references were reviewed on 2026-10-15 for P0.6. All 51 references across
`src/` specify fixed versions, covering 45 distinct packages. `scripts/package-pins.json` records that
inventory; `scripts/check-package-pins.sh` rejects floating versions, unreviewed changes, inconsistent
copies, and stale inventory entries. This inventories direct dependencies; it does not replace a
transitive dependency lockfile or the separate vulnerability review.

The table below covers the dependencies named in P0.6. Versions were checked against NuGet's official
package index at `https://api.nuget.org/v3-flatcontainer/{lowercase-package-id}/index.json`.

| Package | Current direct version | Review result and primary source |
|---|---|---|
| Watson | 7.3.1 | Matches plan; [NuGet](https://www.nuget.org/packages/Watson/7.3.1). |
| Voltaic | 2.3.0 | Matches plan; 2.4.0 is now available, retained the tested plan version; [NuGet](https://www.nuget.org/packages/Voltaic/2.3.0). |
| PolyPrompt | 3.2.0 | Matches plan; [NuGet](https://www.nuget.org/packages/PolyPrompt/3.2.0). |
| PrettyId | 2.0.2 | Matches plan; [NuGet](https://www.nuget.org/packages/PrettyId/2.0.2). |
| Radiant | 0.1.3 | Matches plan; [NuGet](https://www.nuget.org/packages/Radiant/0.1.3). |
| SyslogLogging | 2.4.1 | Matches plan; [NuGet](https://www.nuget.org/packages/SyslogLogging/2.4.1). |
| Blobject.Core, Blobject.AmazonS3, Blobject.AzureBlob, Blobject.GoogleCloud | 6.1.2 | Matches plan; [NuGet Core](https://www.nuget.org/packages/Blobject.Core/6.1.2), [Amazon S3](https://www.nuget.org/packages/Blobject.AmazonS3/6.1.2), [Azure Blob](https://www.nuget.org/packages/Blobject.AzureBlob/6.1.2), [Google Cloud](https://www.nuget.org/packages/Blobject.GoogleCloud/6.1.2). |
| Touchstone.Core, Touchstone.Cli, Touchstone.XunitAdapter, Touchstone.NunitAdapter | 0.2.1 | D-034 explicitly selects 0.2.1 instead of the original 0.1.12 requirement; [NuGet Core](https://www.nuget.org/packages/Touchstone.Core/0.2.1). |
| Microsoft.Data.Sqlite | 10.0.13 | Latest stable in official index at review; [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.13). |
| Npgsql | 10.0.3 | Latest stable in official index at review; [NuGet](https://www.nuget.org/packages/Npgsql/10.0.3). |
| MySqlConnector | 2.6.2 | Latest stable in official index at review; [NuGet](https://www.nuget.org/packages/MySqlConnector/2.6.2). |
| Microsoft.Data.SqlClient | 7.1.1 | Latest stable in official index at review; [NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient/7.1.1). |
| SSH.NET | 2026.0.0 | Latest stable in official index at review; [NuGet](https://www.nuget.org/packages/SSH.NET/2026.0.0). |

Three packages named in the initial design are not runtime dependencies. Adding unused references would
not change their replacement implementations:

| Initial design package | Current implementation and evidence |
|---|---|
| Padlock 1.2.1 | `ConversationLockManager` uses keyed `SemaphoreSlim` gates for conversation turns (P6.20). `ConnectorSessionPool` uses the same primitive for resource concurrency (P3.9). |
| Caching | `ConnectorSessionPool` uses concurrent dictionaries, idle eviction, and a configurable least-recently-used limit. No `Caching` API is used. Official NuGet latest stable was 5.1.4 at review. |
| RestWrapper | Logging, HTTP, and observability connectors use shared `HttpClient` instances for request control, rate-limit headers, and streaming. `HttpConnectorClients` and the P4.21 annotation record this choice. Official NuGet latest stable was 3.3.2 at review. |

No dependency versions or the product version were changed by this review. A future intentional
dependency change should update its project references and the reviewed inventory together, then
restore, build, and run the affected suites.
