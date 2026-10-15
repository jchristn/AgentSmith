namespace Test.Shared.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Storage;
    using Touchstone.Core;

    /// <summary>
    /// Testbed cases for one storage provider against docker/testbed/storage (MinIO, Azurite, or fake-gcs-server). Every
    /// tool is exercised. Objects written by the suite live under a per-run prefix and are removed afterwards. Skipped
    /// unless HUGO_TESTBED=1.
    /// </summary>
    public class StorageTestbedCases : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Provider under test.
        /// </summary>
        public StorageProviderEnum Provider { get; }

        /// <summary>
        /// Suite identifier, for example <c>testbed.storage.s3</c>.
        /// </summary>
        public string SuiteId { get; }

        #endregion

        #region Private-Members

        private const string Readme = "Hugo storage testbed fixture.\nThis object is plain text and is safe to read.\n";
        private static readonly string[] _FixtureKeys = new string[]
        {
            "binary/blob.bin", "data/config.json", "data/nested/deep/note.md", "data/records.csv", "logs/2026/10/14/app.log", "readme.txt"
        };

        private readonly StorageConnectorBase _Connector;
        private readonly NoPoolSessionPool _Pool = new NoPoolSessionPool();
        private readonly string _RunPrefix;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageTestbedCases(StorageProviderEnum provider)
        {
            Provider = provider;
            SuiteId = "testbed.storage." + StorageTestbed.TypeOf(provider);
            _Connector = StorageTestbed.Connector(provider);
            _RunPrefix = "hugo-tests/" + Guid.NewGuid().ToString("N").Substring(0, 12) + "/";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("validate", "ValidateAsync accepts the testbed resource", Validate));
            cases.Add(Case("test-connection", "TestConnectionAsync lists one object and reports latency", TestConnection));
            cases.Add(Case("test-connection-unreachable", "TestConnectionAsync reports connection_failed for a closed port", TestConnectionUnreachable));
            cases.Add(Case("list-folders", "storage_list with delimiter shows folders and root objects", ListFolders));
            cases.Add(Case("list-flat", "storage_list without delimiter lists every key under a prefix", ListFlat));
            cases.Add(Case("list-paging", "storage_list pages with continuation tokens without gaps or duplicates", ListPaging));
            cases.Add(Case("list-paging-folders", "storage_list pages folder listings", ListPagingFolders));
            cases.Add(Case("list-row-limit", "storage_list honors the output row limit", ListRowLimit));
            cases.Add(Case("get-metadata", "storage_get_metadata returns size and content type", GetMetadata));
            cases.Add(Case("get-metadata-missing", "storage_get_metadata returns not_found", GetMetadataMissing));
            cases.Add(Case("read-text", "storage_read returns textual objects as text", ReadText));
            cases.Add(Case("read-binary", "storage_read returns binary objects as base64", ReadBinary));
            cases.Add(Case("read-range", "storage_read honors offset and length", ReadRange));
            cases.Add(Case("read-truncated", "storage_read truncates to limits and continues with nextOffset", ReadTruncated));
            cases.Add(Case("stats", "storage_stats counts objects and bytes", Stats));
            cases.Add(Case("stats-capped", "storage_stats flags a capped scan as truncated", StatsCapped));
            cases.Add(Case("write-create-overwrite", "storage_write creates, refuses overwrite by default, and overwrites when asked", WriteCreateOverwrite));
            cases.Add(Case("write-base64", "storage_write stores base64 content byte for byte", WriteBase64));
            cases.Add(Case("copy", "storage_copy copies content and refuses to replace by default", Copy));
            cases.Add(Case("delete", "storage_delete removes one object and reports not_found afterwards", Delete));
            cases.Add(Case("presign-get", "storage_presign_url returns a working GET URL", PresignGet));
            cases.Add(Case("presign-put", "storage_presign_url returns a PUT URL", PresignPut));
            cases.Add(Case("prefix-restriction", "A restricted resource only sees and touches its prefix", PrefixRestriction));
            cases.Add(Case("bad-credentials", "Wrong credentials produce permission_denied", BadCredentials));
            cases.Add(Case("cancellation", "A cancelled call propagates OperationCanceledException", Cancellation));
            return new TestSuiteDescriptor(
                SuiteId,
                "Storage testbed: " + _Connector.Descriptor.DisplayName,
                cases,
                null,
                StorageTestbed.Enabled ? CleanupAsync : null);
        }

        /// <summary>
        /// Dispose pooled sessions.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pooled sessions.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Pool.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            if (!StorageTestbed.Enabled) return new TestCaseDescriptor(SuiteId, id, name, body, null, true, StorageTestbed.SkipReason);
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private ConnectorResourceContext Resource(string variant = "default", Dictionary<string, string>? settings = null, bool badCredentials = false)
        {
            return StorageTestbed.Resource(Provider, variant, _Pool, settings, badCredentials);
        }

        private ConnectorInvocationContext Context(OutputLimits? limits = null, string variant = "default", Dictionary<string, string>? settings = null)
        {
            return StorageTestbed.Context(Resource(variant, settings), limits);
        }

        private Task<ToolResult> Call(string tool, string json, CancellationToken token, ConnectorInvocationContext? context = null)
        {
            return StorageTestbed.InvokeAsync(_Connector, context ?? Context(), tool, json, token);
        }

        private static T Structured<T>(ToolResult result) where T : class
        {
            StorageTestAssert.True(!String.IsNullOrEmpty(result.StructuredJson), "structured content present");
            T? value = StorageJson.Deserialize<T>(result.StructuredJson!);
            StorageTestAssert.True(value != null, "structured content parses");
            return value!;
        }

        private async ValueTask CleanupAsync(CancellationToken token)
        {
            try
            {
                using (StorageSession session = StorageClientFactory.CreateSession(Provider, Resource("cleanup")))
                {
                    List<string> keys = new List<string>();
                    EnumerationFilter filter = new EnumerationFilter();
                    filter.Prefix = _RunPrefix;
                    await foreach (BlobMetadata md in session.Client.EnumerateAsync(filter, token).ConfigureAwait(false)) keys.Add(md.Key);
                    foreach (string key in keys) await session.Client.DeleteAsync(key, token).ConfigureAwait(false);
                }
            }
            finally
            {
                Dispose();
            }
        }

        private async Task Validate(CancellationToken token)
        {
            ConnectorValidationResult result = await _Connector.ValidateAsync(Resource(), token).ConfigureAwait(false);
            StorageTestAssert.True(result.IsValid, "valid: " + String.Join("; ", result.Errors.Select(e => e.Field + " " + e.Message)));
        }

        private async Task TestConnection(CancellationToken token)
        {
            ConnectorHealthResult health = await _Connector.TestConnectionAsync(Resource(), token).ConfigureAwait(false);
            StorageTestAssert.True(health.Success, "success: " + health.ErrorCode + " " + health.Message);
            StorageTestAssert.True(health.LatencyMs > 0, "latency reported");
            StorageTestAssert.Equal(StorageTestbed.Container, health.Details["container"], "container detail");
            StorageTestAssert.True(health.Details.ContainsKey("sampleKey"), "sample key");
        }

        private async Task TestConnectionUnreachable(CancellationToken token)
        {
            string key = Provider == StorageProviderEnum.GoogleCloud ? "customEndpoint" : "endpoint";
            string value = Provider == StorageProviderEnum.AzureBlob ? "http://127.0.0.1:1/devstoreaccount1" : "http://127.0.0.1:1";
            StorageConnectorBase connector = StorageTestbed.Connector(Provider);
            connector.TestTimeout = TimeSpan.FromSeconds(20);
            ConnectorHealthResult health = await connector.TestConnectionAsync(Resource("unreachable", new Dictionary<string, string> { { key, value } }), token).ConfigureAwait(false);
            StorageTestAssert.True(!health.Success, "fails");
            StorageTestAssert.True(health.ErrorCode == "connection_failed", "connection_failed, got " + health.ErrorCode + ": " + health.Message);
        }

        private async Task ListFolders(CancellationToken token)
        {
            ToolResult result = await Call("storage_list", "{\"delimiter\":\"/\"}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "list");
            StorageListResult list = Structured<StorageListResult>(result);
            HashSet<string> keys = new HashSet<string>(list.Entries.Select(e => e.Key), StringComparer.Ordinal);
            foreach (string folder in new string[] { "binary/", "data/", "logs/" })
                StorageTestAssert.True(keys.Contains(folder) && list.Entries.First(e => e.Key == folder).IsFolder, "folder " + folder);
            StorageTestAssert.True(keys.Contains("readme.txt") && !list.Entries.First(e => e.Key == "readme.txt").IsFolder, "root object");
            StorageTestAssert.True(!keys.Any(k => k.StartsWith("data/", StringComparison.Ordinal) && k != "data/"), "nested keys collapsed");

            ToolResult nested = await Call("storage_list", "{\"prefix\":\"data/\",\"delimiter\":\"/\"}", token).ConfigureAwait(false);
            StorageListResult inner = Structured<StorageListResult>(nested);
            StorageTestAssert.True(inner.Entries.Select(e => e.Key).SequenceEqual(new string[] { "data/config.json", "data/nested/", "data/records.csv" }), "data/ level: " + String.Join(",", inner.Entries.Select(e => e.Key)));
            StorageTestAssert.True(nested.Text.Contains("data/nested/", StringComparison.Ordinal) && nested.Text.Contains("folder", StringComparison.Ordinal), "text table");
        }

        private async Task ListFlat(CancellationToken token)
        {
            ToolResult result = await Call("storage_list", "{\"prefix\":\"data/\"}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "list");
            StorageListResult list = Structured<StorageListResult>(result);
            StorageTestAssert.True(list.Entries.Select(e => e.Key).SequenceEqual(new string[] { "data/config.json", "data/nested/deep/note.md", "data/records.csv" }), "flat keys: " + String.Join(",", list.Entries.Select(e => e.Key)));
            StorageTestAssert.Equal(84L, list.Entries[0].Size, "config size");
            StorageTestAssert.True(list.ContinuationToken == null, "complete");
        }

        private async Task ListPaging(CancellationToken token)
        {
            List<string> seen = new List<string>();
            string? continuation = null;
            int pages = 0;
            do
            {
                string json = "{\"maxResults\":2" + (continuation == null ? String.Empty : ",\"continuationToken\":" + StorageTestbed.Quote(continuation)) + "}";
                ToolResult result = await Call("storage_list", json, token).ConfigureAwait(false);
                StorageTestAssert.Ok(result, "page " + pages);
                StorageListResult page = Structured<StorageListResult>(result);
                StorageTestAssert.True(page.Entries.Count <= 2, "page size");
                seen.AddRange(page.Entries.Select(e => e.Key));
                continuation = page.ContinuationToken;
                pages++;
            }
            while (continuation != null && pages < 50);

            StorageTestAssert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count(), "no duplicates");
            foreach (string key in _FixtureKeys) StorageTestAssert.True(seen.Contains(key), "paged listing contains " + key);
            StorageTestAssert.True(pages >= 3, "several pages");
            List<string> sorted = seen.OrderBy(k => k, StringComparer.Ordinal).ToList();
            StorageTestAssert.True(sorted.SequenceEqual(seen), "lexicographic order");
        }

        private async Task ListPagingFolders(CancellationToken token)
        {
            List<string> seen = new List<string>();
            string? continuation = null;
            int pages = 0;
            do
            {
                string json = "{\"delimiter\":\"/\",\"maxResults\":1" + (continuation == null ? String.Empty : ",\"continuationToken\":" + StorageTestbed.Quote(continuation)) + "}";
                ToolResult result = await Call("storage_list", json, token).ConfigureAwait(false);
                StorageTestAssert.Ok(result, "page " + pages);
                StorageListResult page = Structured<StorageListResult>(result);
                seen.AddRange(page.Entries.Select(e => e.Key));
                continuation = page.ContinuationToken;
                pages++;
            }
            while (continuation != null && pages < 50);
            StorageTestAssert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count(), "no duplicate folders: " + String.Join(",", seen));
            foreach (string key in new string[] { "binary/", "data/", "logs/", "readme.txt" }) StorageTestAssert.True(seen.Contains(key), "contains " + key);
        }

        private async Task ListRowLimit(CancellationToken token)
        {
            OutputLimits limits = new OutputLimits();
            limits.MaxRows = 2;
            ToolResult result = await Call("storage_list", "{}", token, Context(limits)).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "list");
            StorageListResult list = Structured<StorageListResult>(result);
            StorageTestAssert.Equal(2, list.Entries.Count, "row limit");
            StorageTestAssert.True(list.ContinuationToken != null && result.Truncated, "more available");
            StorageTestAssert.True(result.Text.Contains("continuationToken", StringComparison.Ordinal), "text tells the model how to continue");
        }

        private async Task GetMetadata(CancellationToken token)
        {
            ToolResult result = await Call("storage_get_metadata", "{\"key\":\"data/config.json\"}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "metadata");
            StorageObjectInfo info = Structured<StorageObjectInfo>(result);
            StorageTestAssert.Equal(84L, info.Size, "size");
            StorageTestAssert.True(info.ContentType != null && info.ContentType.StartsWith("application/json", StringComparison.Ordinal), "content type " + info.ContentType);
            StorageTestAssert.True(info.LastModifiedUtc != null, "modified time");
            StorageTestAssert.True(result.Text.Contains("size: 84 bytes", StringComparison.Ordinal), "text");
        }

        private async Task GetMetadataMissing(CancellationToken token)
        {
            ToolResult result = await Call("storage_get_metadata", "{\"key\":\"data/does-not-exist.txt\"}", token).ConfigureAwait(false);
            StorageTestAssert.Error(result, "not_found", "missing object");
            StorageTestAssert.True(result.Text.Contains("data/does-not-exist.txt", StringComparison.Ordinal), "message names key");
        }

        private async Task ReadText(CancellationToken token)
        {
            ToolResult result = await Call("storage_read", "{\"key\":\"readme.txt\"}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "read");
            StorageReadResult read = Structured<StorageReadResult>(result);
            StorageTestAssert.Equal("text", read.Encoding, "encoding");
            StorageTestAssert.Equal(Readme, read.Content, "content");
            StorageTestAssert.True(!read.Truncated && read.NextOffset == null && !result.Truncated, "complete");

            ToolResult json = await Call("storage_read", "{\"key\":\"data/config.json\"}", token).ConfigureAwait(false);
            StorageReadResult jsonRead = Structured<StorageReadResult>(json);
            StorageTestAssert.True(jsonRead.Encoding == "text" && jsonRead.Content.Contains("\"checkout\"", StringComparison.Ordinal), "json as text");
            StorageTestAssert.True(json.Text.Contains("\"replicas\": 3", StringComparison.Ordinal), "text output carries content");

            ToolResult forced = await Call("storage_read", "{\"key\":\"readme.txt\",\"encoding\":\"base64\"}", token).ConfigureAwait(false);
            StorageReadResult forcedRead = Structured<StorageReadResult>(forced);
            StorageTestAssert.Equal(Readme, Encoding.UTF8.GetString(Convert.FromBase64String(forcedRead.Content)), "forced base64");
        }

        private async Task ReadBinary(CancellationToken token)
        {
            ToolResult result = await Call("storage_read", "{\"key\":\"binary/blob.bin\"}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "read");
            StorageReadResult read = Structured<StorageReadResult>(result);
            StorageTestAssert.Equal("base64", read.Encoding, "encoding");
            byte[] bytes = Convert.FromBase64String(read.Content);
            StorageTestAssert.True(bytes.SequenceEqual(Enumerable.Range(0, 256).Select(i => (byte)i)), "bytes");
            StorageTestAssert.Equal(256L, read.BytesReturned, "bytes returned");
        }

        private async Task ReadRange(CancellationToken token)
        {
            ToolResult result = await Call("storage_read", "{\"key\":\"readme.txt\",\"offset\":5,\"length\":7}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "read");
            StorageReadResult read = Structured<StorageReadResult>(result);
            StorageTestAssert.Equal("storage", read.Content, "range content");
            StorageTestAssert.True(read.Truncated && read.NextOffset == 12, "next offset");

            ToolResult past = await Call("storage_read", "{\"key\":\"readme.txt\",\"offset\":100000}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(past, "past end");
            StorageTestAssert.True(Structured<StorageReadResult>(past).BytesReturned == 0 && past.Text.Contains("past the end", StringComparison.Ordinal), "nothing past end");

            ToolResult binaryRange = await Call("storage_read", "{\"key\":\"binary/blob.bin\",\"offset\":250}", token).ConfigureAwait(false);
            byte[] tail = Convert.FromBase64String(Structured<StorageReadResult>(binaryRange).Content);
            StorageTestAssert.True(tail.SequenceEqual(new byte[] { 250, 251, 252, 253, 254, 255 }), "binary tail");
        }

        private async Task ReadTruncated(CancellationToken token)
        {
            string key = _RunPrefix + "big.txt";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 400; i++) sb.Append("line ").Append(i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)).Append(" ").Append(new string('x', 10)).Append('\n');
            string text = sb.ToString();
            ToolResult write = await Call("storage_write", "{\"key\":" + StorageTestbed.Quote(key) + ",\"content\":" + StorageTestbed.Quote(text) + "}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(write, "seed big object");

            Dictionary<string, string> small = new Dictionary<string, string> { { "maxObjectReadBytes", "2048" } };
            OutputLimits limits = new OutputLimits();
            limits.MaxBytes = 4096;
            ConnectorInvocationContext context = Context(limits, "small-read", small);

            StringBuilder assembled = new StringBuilder();
            long offset = 0;
            int calls = 0;
            while (true)
            {
                ToolResult result = await Call("storage_read", "{\"key\":" + StorageTestbed.Quote(key) + ",\"offset\":" + offset + "}", token, context).ConfigureAwait(false);
                StorageTestAssert.Ok(result, "read at " + offset);
                StorageReadResult read = Structured<StorageReadResult>(result);
                StorageTestAssert.True(read.BytesReturned <= 2048, "bounded by maxObjectReadBytes");
                assembled.Append(read.Content);
                calls++;
                if (calls == 1) StorageTestAssert.True(result.Truncated && read.Truncated && result.Text.Contains("Continue with offset", StringComparison.Ordinal), "first read truncated");
                if (read.NextOffset == null) break;
                offset = read.NextOffset.Value;
                StorageTestAssert.True(calls < 20, "terminates");
            }
            StorageTestAssert.Equal(text, assembled.ToString(), "reassembled content");
            StorageTestAssert.True(calls >= 3, "several ranges");

            OutputLimits fewLines = new OutputLimits();
            fewLines.MaxLines = 10;
            ToolResult lines = await Call("storage_read", "{\"key\":" + StorageTestbed.Quote(key) + "}", token, Context(fewLines)).ConfigureAwait(false);
            StorageReadResult lineRead = Structured<StorageReadResult>(lines);
            StorageTestAssert.Equal(10, lineRead.Content.Count(c => c == '\n'), "line limit");
            StorageTestAssert.Equal((long)Encoding.UTF8.GetByteCount(lineRead.Content), lineRead.NextOffset!.Value, "next offset after line cut");
        }

        private async Task Stats(CancellationToken token)
        {
            ToolResult result = await Call("storage_stats", "{\"prefix\":\"data/\"}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "stats");
            StorageStatsResult stats = Structured<StorageStatsResult>(result);
            StorageTestAssert.Equal(3L, stats.ObjectCount, "count");
            StorageTestAssert.Equal(199L, stats.TotalBytes, "bytes");
            StorageTestAssert.Equal("data/config.json", stats.LargestKey, "largest");
            StorageTestAssert.True(!stats.Truncated, "not truncated");
            StorageTestAssert.True(stats.Groups.Any(g => g.Prefix == "data/nested/" && g.ObjectCount == 1 && g.TotalBytes == 60), "nested group");

            ToolResult root = await Call("storage_stats", "{}", token).ConfigureAwait(false);
            StorageStatsResult all = Structured<StorageStatsResult>(root);
            StorageTestAssert.True(all.ObjectCount >= 6 && all.TotalBytes >= 699, "root totals " + all.ObjectCount + "/" + all.TotalBytes);
            StorageTestAssert.True(root.Text.Contains("totalBytes", StringComparison.Ordinal), "text");
        }

        private async Task StatsCapped(CancellationToken token)
        {
            ToolResult result = await Call("storage_stats", "{\"maxObjects\":2}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "stats");
            StorageStatsResult stats = Structured<StorageStatsResult>(result);
            StorageTestAssert.True(stats.Truncated && result.Truncated && stats.ObjectCount == 2, "capped");
        }

        private async Task WriteCreateOverwrite(CancellationToken token)
        {
            string key = _RunPrefix + "notes/write.txt";
            string args = "{\"key\":" + StorageTestbed.Quote(key) + ",\"content\":\"first version\"}";
            ToolResult created = await Call("storage_write", args, token).ConfigureAwait(false);
            StorageTestAssert.Ok(created, "create");
            StorageMutationResult mutation = Structured<StorageMutationResult>(created);
            StorageTestAssert.True(!mutation.Overwrote && mutation.Bytes == 13, "created");

            ToolResult refused = await Call("storage_write", "{\"key\":" + StorageTestbed.Quote(key) + ",\"content\":\"second\"}", token).ConfigureAwait(false);
            StorageTestAssert.Error(refused, "already_exists", "refuses overwrite");

            string overwriteArgs = "{\"key\":" + StorageTestbed.Quote(key) + ",\"content\":\"second version\",\"overwrite\":true}";
            EffectClassification classification = StorageTestbed.Classify(_Connector, Context(), "storage_write", overwriteArgs);
            StorageTestAssert.Equal(EffectEnum.Destructive, classification.Effect, "overwrite classified Destructive");
            ToolResult replaced = await Call("storage_write", overwriteArgs, token).ConfigureAwait(false);
            StorageTestAssert.Ok(replaced, "overwrite");
            StorageTestAssert.True(Structured<StorageMutationResult>(replaced).Overwrote, "overwrote flag");

            ToolResult read = await Call("storage_read", "{\"key\":" + StorageTestbed.Quote(key) + "}", token).ConfigureAwait(false);
            StorageTestAssert.Equal("second version", Structured<StorageReadResult>(read).Content, "content replaced");
            ToolResult meta = await Call("storage_get_metadata", "{\"key\":" + StorageTestbed.Quote(key) + "}", token).ConfigureAwait(false);
            StorageTestAssert.True((Structured<StorageObjectInfo>(meta).ContentType ?? String.Empty).StartsWith("text/plain", StringComparison.Ordinal), "guessed content type");

            ToolResult folder = await Call("storage_write", "{\"key\":" + StorageTestbed.Quote(_RunPrefix + "dir/") + ",\"content\":\"\"}", token).ConfigureAwait(false);
            StorageTestAssert.Error(folder, "invalid_argument", "folder keys refused");
        }

        private async Task WriteBase64(CancellationToken token)
        {
            string key = _RunPrefix + "bin/data.bin";
            byte[] bytes = new byte[] { 0, 1, 2, 3, 255, 254, 0, 10, 13 };
            ToolResult written = await Call("storage_write", "{\"key\":" + StorageTestbed.Quote(key) + ",\"content\":" + StorageTestbed.Quote(Convert.ToBase64String(bytes)) + ",\"encoding\":\"base64\"}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(written, "write");
            StorageTestAssert.Equal("application/octet-stream", Structured<StorageMutationResult>(written).ContentType, "content type");
            ToolResult read = await Call("storage_read", "{\"key\":" + StorageTestbed.Quote(key) + "}", token).ConfigureAwait(false);
            StorageReadResult r = Structured<StorageReadResult>(read);
            StorageTestAssert.True(r.Encoding == "base64" && Convert.FromBase64String(r.Content).SequenceEqual(bytes), "round trip");

            ToolResult bad = await Call("storage_write", "{\"key\":" + StorageTestbed.Quote(_RunPrefix + "bin/bad.bin") + ",\"content\":\"***\",\"encoding\":\"base64\"}", token).ConfigureAwait(false);
            StorageTestAssert.Error(bad, "invalid_argument", "bad base64");
        }

        private async Task Copy(CancellationToken token)
        {
            string destination = _RunPrefix + "copies/readme.txt";
            string args = "{\"sourceKey\":\"readme.txt\",\"destinationKey\":" + StorageTestbed.Quote(destination) + "}";
            ToolResult copied = await Call("storage_copy", args, token).ConfigureAwait(false);
            StorageTestAssert.Ok(copied, "copy");
            StorageMutationResult mutation = Structured<StorageMutationResult>(copied);
            StorageTestAssert.True(mutation.SourceKey == "readme.txt" && mutation.Bytes == Readme.Length && !mutation.Overwrote, "copy result");
            ToolResult read = await Call("storage_read", "{\"key\":" + StorageTestbed.Quote(destination) + "}", token).ConfigureAwait(false);
            StorageTestAssert.Equal(Readme, Structured<StorageReadResult>(read).Content, "copied content");

            ToolResult again = await Call("storage_copy", args, token).ConfigureAwait(false);
            StorageTestAssert.Error(again, "already_exists", "refuses to replace");
            ToolResult replace = await Call("storage_copy", "{\"sourceKey\":\"data/config.json\",\"destinationKey\":" + StorageTestbed.Quote(destination) + ",\"overwrite\":true}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(replace, "replace");
            StorageTestAssert.True(Structured<StorageMutationResult>(replace).Overwrote, "overwrote");

            ToolResult missing = await Call("storage_copy", "{\"sourceKey\":\"nope/missing.txt\",\"destinationKey\":" + StorageTestbed.Quote(_RunPrefix + "copies/x") + "}", token).ConfigureAwait(false);
            StorageTestAssert.Error(missing, "not_found", "missing source");
            ToolResult same = await Call("storage_copy", "{\"sourceKey\":\"readme.txt\",\"destinationKey\":\"readme.txt\"}", token).ConfigureAwait(false);
            StorageTestAssert.Error(same, "invalid_argument", "same key");
        }

        private async Task Delete(CancellationToken token)
        {
            string key = _RunPrefix + "delete/me.txt";
            StorageTestAssert.Ok(await Call("storage_write", "{\"key\":" + StorageTestbed.Quote(key) + ",\"content\":\"bye\"}", token).ConfigureAwait(false), "seed");
            ToolResult deleted = await Call("storage_delete", "{\"key\":" + StorageTestbed.Quote(key) + "}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(deleted, "delete");
            StorageMutationResult mutation = Structured<StorageMutationResult>(deleted);
            StorageTestAssert.True(mutation.Operation == "delete" && mutation.Key == key && mutation.Bytes == 3, "delete result");
            StorageTestAssert.Error(await Call("storage_get_metadata", "{\"key\":" + StorageTestbed.Quote(key) + "}", token).ConfigureAwait(false), "not_found", "gone");
            StorageTestAssert.Error(await Call("storage_delete", "{\"key\":" + StorageTestbed.Quote(key) + "}", token).ConfigureAwait(false), "not_found", "second delete");
        }

        private async Task PresignGet(CancellationToken token)
        {
            ToolResult result = await Call("storage_presign_url", "{\"key\":\"data/config.json\",\"expirySeconds\":300}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "presign");
            StoragePresignResult presign = Structured<StoragePresignResult>(result);
            StorageTestAssert.True(presign.Method == "GET" && presign.ExpiresUtc > DateTime.UtcNow.AddSeconds(200), "method and expiry");
            StorageTestAssert.True(presign.Url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal), "url points at the testbed: " + presign.Url);
            string signatureMarker = Provider == StorageProviderEnum.AmazonS3 ? "X-Amz-Signature=" : (Provider == StorageProviderEnum.AzureBlob ? "sig=" : "X-Goog-Signature=");
            StorageTestAssert.True(presign.Url.Contains(signatureMarker, StringComparison.Ordinal), "signed URL");

            using (HttpClient http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                using (HttpResponseMessage response = await http.GetAsync(presign.Url, token).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    StorageTestAssert.True(response.IsSuccessStatusCode, "GET via presigned URL: " + (int)response.StatusCode);
                    StorageTestAssert.True(body.Contains("\"checkout\"", StringComparison.Ordinal), "body");
                }
            }

            ToolResult missing = await Call("storage_presign_url", "{\"key\":\"nope/missing.txt\"}", token).ConfigureAwait(false);
            StorageTestAssert.Error(missing, "not_found", "no URL for a missing object");
        }

        private async Task PresignPut(CancellationToken token)
        {
            string key = _RunPrefix + "presigned/upload.txt";
            ToolResult result = await Call("storage_presign_url", "{\"key\":" + StorageTestbed.Quote(key) + ",\"method\":\"PUT\",\"expirySeconds\":120}", token).ConfigureAwait(false);
            StorageTestAssert.Ok(result, "presign put");
            StoragePresignResult presign = Structured<StoragePresignResult>(result);
            StorageTestAssert.True(presign.Method == "PUT" && presign.Url.Length > 20, "put url");
            if (Provider == StorageProviderEnum.GoogleCloud) return;

            using (HttpClient http = new HttpClient())
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, presign.Url))
            {
                request.Content = new StringContent("uploaded through a presigned URL", Encoding.UTF8);
                request.Content.Headers.ContentType = null;
                if (Provider == StorageProviderEnum.AzureBlob) request.Headers.Add("x-ms-blob-type", "BlockBlob");
                using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                {
                    StorageTestAssert.True(response.IsSuccessStatusCode, "PUT via presigned URL: " + (int)response.StatusCode);
                }
            }
            ToolResult read = await Call("storage_read", "{\"key\":" + StorageTestbed.Quote(key) + ",\"encoding\":\"text\"}", token).ConfigureAwait(false);
            StorageTestAssert.Equal("uploaded through a presigned URL", Structured<StorageReadResult>(read).Content, "uploaded content");
        }

        private async Task PrefixRestriction(CancellationToken token)
        {
            Dictionary<string, string> restricted = new Dictionary<string, string> { { "prefixRestriction", "data/" } };
            ConnectorInvocationContext context = Context(null, "restricted", restricted);

            ToolResult list = await Call("storage_list", "{}", token, context).ConfigureAwait(false);
            StorageTestAssert.Ok(list, "list");
            StorageListResult entries = Structured<StorageListResult>(list);
            StorageTestAssert.True(entries.Prefix == "data/" && entries.Entries.Count == 3 && entries.Entries.All(e => e.Key.StartsWith("data/", StringComparison.Ordinal)), "only data/");

            StorageTestAssert.Error(await Call("storage_read", "{\"key\":\"readme.txt\"}", token, context).ConfigureAwait(false), "permission_denied", "read outside");
            StorageTestAssert.Error(await Call("storage_list", "{\"prefix\":\"logs/\"}", token, context).ConfigureAwait(false), "permission_denied", "list outside");
            StorageTestAssert.Error(await Call("storage_delete", "{\"key\":\"readme.txt\"}", token, context).ConfigureAwait(false), "permission_denied", "delete outside");
            StorageTestAssert.Error(await Call("storage_copy", "{\"sourceKey\":\"data/config.json\",\"destinationKey\":\"elsewhere.json\"}", token, context).ConfigureAwait(false), "permission_denied", "copy outside");
            StorageTestAssert.Error(await Call("storage_read", "{\"key\":\"data/../readme.txt\"}", token, context).ConfigureAwait(false), "invalid_argument", "dot segments");

            StorageStatsResult stats = Structured<StorageStatsResult>(await Call("storage_stats", "{}", token, context).ConfigureAwait(false));
            StorageTestAssert.True(stats.Prefix == "data/" && stats.ObjectCount == 3, "stats scoped");

            ConnectorHealthResult health = await _Connector.TestConnectionAsync(Resource("restricted-health", restricted), token).ConfigureAwait(false);
            StorageTestAssert.True(health.Success && health.Details["sampleKey"].StartsWith("data/", StringComparison.Ordinal), "test connection scoped");
        }

        private async Task BadCredentials(CancellationToken token)
        {
            if (Provider == StorageProviderEnum.GoogleCloud) return;
            ConnectorInvocationContext context = StorageTestbed.Context(Resource("bad-credentials", null, true));
            ToolResult result = await Call("storage_list", "{}", token, context).ConfigureAwait(false);
            StorageTestAssert.Error(result, "permission_denied", "wrong credentials");
            ConnectorHealthResult health = await _Connector.TestConnectionAsync(Resource("bad-credentials-health", null, true), token).ConfigureAwait(false);
            StorageTestAssert.True(!health.Success && health.ErrorCode == "permission_denied", "health reports permission_denied, got " + health.ErrorCode);
        }

        private async Task Cancellation(CancellationToken token)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool threw = false;
                try
                {
                    await Call("storage_stats", "{}", cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
                StorageTestAssert.True(threw, "cancelled");
            }
        }

        #endregion
    }
}
