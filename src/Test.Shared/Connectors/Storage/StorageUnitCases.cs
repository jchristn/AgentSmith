namespace Test.Shared.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Storage;
    using Touchstone.Core;

    /// <summary>
    /// Offline unit cases for the storage connectors: descriptors, schemas, prefix restriction, continuation tokens,
    /// content detection, classification, validation, and client factory mapping. No network access.
    /// </summary>
    public static class StorageUnitCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "connectors.storage";

        #endregion

        #region Private-Members

        private static readonly StorageProviderEnum[] _Providers = new StorageProviderEnum[]
        {
            StorageProviderEnum.AmazonS3,
            StorageProviderEnum.AzureBlob,
            StorageProviderEnum.GoogleCloud
        };

        private static readonly string[] _ToolNames = new string[]
        {
            "storage_list", "storage_get_metadata", "storage_read", "storage_stats",
            "storage_presign_url", "storage_write", "storage_copy", "storage_delete"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the unit suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("descriptors", "Descriptors declare type, family, category, icon, and instructions", Descriptors));
            cases.Add(Case("settings-schema", "Settings schemas carry provider and common settings with defaults", SettingsSchema));
            cases.Add(Case("secret-slots", "Secret slots match the plan", SecretSlots));
            cases.Add(Case("tool-set-shared", "All three connectors expose the same tools with identical schemas", ToolSetShared));
            cases.Add(Case("schemas-parse", "Every tool input schema is a valid JSON object schema", SchemasParse));
            cases.Add(Case("key-policy-valid", "Well-formed keys inside the restriction are accepted", KeyPolicyValid));
            cases.Add(Case("key-policy-restriction", "Keys and prefixes outside the restriction are permission_denied", KeyPolicyRestriction));
            cases.Add(Case("key-policy-malformed", "Dot segments, leading slash, backslash, and control characters are rejected", KeyPolicyMalformed));
            cases.Add(Case("continuation-token", "Continuation tokens round-trip and are bound to the prefix", ContinuationToken));
            cases.Add(Case("list-deep-paging", "storage_list pages far past maxScanObjects because skipped keys do not use the scan budget", ListDeepPaging));
            cases.Add(Case("list-skip-budget", "Paging stops with scan_limit only when the skip budget runs out", ListSkipBudget));
            cases.Add(Case("content-detection", "Textual content types and UTF-8 detection", ContentDetection));
            cases.Add(Case("classify-read-tools", "Read tools classify as Read with the full key in the summary", ClassifyReadTools));
            cases.Add(Case("classify-write", "storage_write is Write, or Destructive with overwrite=true", ClassifyWrite));
            cases.Add(Case("classify-copy", "storage_copy is Write, or Destructive with overwrite=true", ClassifyCopy));
            cases.Add(Case("classify-delete", "storage_delete is Destructive and names the full key and bucket", ClassifyDelete));
            cases.Add(Case("classify-presign", "storage_presign_url GET is Write and PUT is Destructive", ClassifyPresign));
            cases.Add(Case("classify-rejects-outside-prefix", "Calls outside the prefix restriction are rejected at classification", ClassifyRejects));
            cases.Add(Case("validate-s3", "S3 validation: bucket, endpoint, key pair, session token", ValidateS3));
            cases.Add(Case("validate-azure", "Azure validation: exactly one credential, account name, key format", ValidateAzure));
            cases.Add(Case("validate-gcs", "GCS validation: service account key shape", ValidateGcs));
            cases.Add(Case("validate-prefix-restriction", "A malformed prefix restriction fails validation", ValidatePrefixRestriction));
            cases.Add(Case("factory-s3-settings", "S3 settings map endpoint scheme to TLS and credentials", FactoryS3Settings));
            cases.Add(Case("s3-authentication-errors", "Typed AWS errors distinguish credentials, region and policy failures without leaking signing material", S3AuthenticationErrors));
            cases.Add(Case("s3-regional-signing", "AWS us-east-2 uses regional virtual-hosted addressing and SigV4 credentials without a custom endpoint", S3RegionalSigning));
            cases.Add(Case("factory-endpoints", "Azure and GCS endpoints are normalized", FactoryEndpoints));
            cases.Add(Case("factory-sessions", "Sessions build offline for all providers, with presigners", FactorySessions));
            cases.Add(Case("options-clamp", "Options apply defaults and clamp to ranges", OptionsClamp));
            cases.Add(Case("execute-permission-denied", "Execution refuses keys outside the restriction with permission_denied", ExecutePermissionDenied));
            cases.Add(Case("execute-timeout", "A call to a server that never answers returns a timeout error", ExecuteTimeout));
            cases.Add(Case("execute-cancelled", "A cancelled token propagates OperationCanceledException", ExecuteCancelled));
            cases.Add(Case("test-connection-invalid", "TestConnectionAsync reports invalid configuration without connecting", TestConnectionInvalid));
            return new TestSuiteDescriptor(SuiteId, "Storage connectors (offline)", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static ConnectorResourceContext Offline(StorageProviderEnum provider, Dictionary<string, string>? settings = null)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            switch (provider)
            {
                case StorageProviderEnum.AmazonS3:
                    values["bucket"] = "acme-assets";
                    values["endpoint"] = "http://127.0.0.1:9";
                    secrets["accessKeyPair"] = new Dictionary<string, string> { { "accessKeyId", "AKIDEXAMPLE" }, { "secretAccessKey", "secret-example" } };
                    break;
                case StorageProviderEnum.AzureBlob:
                    values["accountName"] = "acmeassets";
                    values["container"] = "acme-assets";
                    values["endpoint"] = "http://127.0.0.1:9/acmeassets";
                    secrets["accessKey"] = new Dictionary<string, string> { { "token", StorageTestbed.AzuriteKey } };
                    break;
                default:
                    values["projectId"] = "acme-project";
                    values["bucket"] = "acme-assets";
                    values["customEndpoint"] = "http://127.0.0.1:9";
                    secrets["serviceAccountJson"] = new Dictionary<string, string> { { "json", StorageTestbed.GcsKeyJson() } };
                    break;
            }
            if (settings != null)
            {
                foreach (KeyValuePair<string, string> kv in settings) values[kv.Key] = kv.Value;
            }
            ConnectorResourceContext resource = new ConnectorResourceContext();
            resource.Name = "assets";
            resource.ConnectorType = StorageTestbed.TypeOf(provider);
            resource.Settings = new ResourceSettings(values);
            resource.Secrets = new SecretAccessor(secrets);
            resource.ConfigurationHash = Guid.NewGuid().ToString("N");
            return resource;
        }

        private static ConnectorResourceContext WithSecrets(ConnectorResourceContext resource, Dictionary<string, Dictionary<string, string>> secrets)
        {
            resource.Secrets = new SecretAccessor(secrets);
            return resource;
        }

        private static Task Descriptors(CancellationToken token)
        {
            foreach (StorageProviderEnum provider in _Providers)
            {
                StorageConnectorBase connector = StorageTestbed.Connector(provider);
                ConnectorDescriptor d = connector.Descriptor;
                StorageTestAssert.Equal(StorageTestbed.TypeOf(provider), d.Type, "type");
                StorageTestAssert.Equal("storage", d.Family, "family");
                StorageTestAssert.Equal(ConnectorCategoryEnum.Storage, d.Category, "category");
                StorageTestAssert.Equal("bucket", d.IconKey, "icon");
                StorageTestAssert.Equal("connector." + d.Type + ".instructions", d.DefaultInstructionsKey, "instructions key");
                StorageTestAssert.True(d.DefaultInstructions.Contains("storage_list", StringComparison.Ordinal), d.Type + " instructions mention storage_list");
                StorageTestAssert.True(d.DefaultInstructions.Contains("storage_stats", StringComparison.Ordinal), d.Type + " instructions mention storage_stats");
                StorageTestAssert.True(!d.DefaultInstructions.Contains('\u2014'), d.Type + " instructions contain no em-dash");
                StorageTestAssert.True(!String.IsNullOrEmpty(d.DisplayName) && !String.IsNullOrEmpty(d.Description), "display name and description");
            }
            return Task.CompletedTask;
        }

        private static Task SettingsSchema(CancellationToken token)
        {
            Dictionary<StorageProviderEnum, string[]> expected = new Dictionary<StorageProviderEnum, string[]>
            {
                { StorageProviderEnum.AmazonS3, new string[] { "bucket", "region", "endpoint", "useSsl" } },
                { StorageProviderEnum.AzureBlob, new string[] { "accountName", "container", "endpoint" } },
                { StorageProviderEnum.GoogleCloud, new string[] { "projectId", "bucket", "customEndpoint" } }
            };
            foreach (StorageProviderEnum provider in _Providers)
            {
                ConnectorSettingsSchema schema = StorageTestbed.Connector(provider).Descriptor.SettingsSchema;
                foreach (string key in expected[provider].Concat(new string[] { "prefixRestriction", "maxObjectReadBytes", "maxListResults", "maxScanObjects" }))
                    StorageTestAssert.True(schema.Find(key) != null, provider + " has setting " + key);
                StorageTestAssert.Equal("1048576", schema.Find("maxObjectReadBytes")!.DefaultValue, "read default");
                StorageTestAssert.Equal("1000", schema.Find("maxListResults")!.DefaultValue, "list default");
                StorageTestAssert.True(schema.Find("forcePathStyle") == null, "no forcePathStyle (Blobject decides)");
                StorageTestAssert.True(schema.Find("useInstanceRole") == null, "no useInstanceRole (Blobject has no ambient credentials)");
            }
            StorageTestAssert.True(StorageTestbed.Connector(StorageProviderEnum.AmazonS3).Descriptor.SettingsSchema.Find("bucket")!.Required, "s3 bucket required");
            StorageTestAssert.True(StorageTestbed.Connector(StorageProviderEnum.AzureBlob).Descriptor.SettingsSchema.Find("container")!.Required, "azure container required");
            return Task.CompletedTask;
        }

        private static Task SecretSlots(CancellationToken token)
        {
            SecretSlotDescriptor s3 = StorageTestbed.Connector(StorageProviderEnum.AmazonS3).Descriptor.SecretSlots.Single();
            StorageTestAssert.Equal("accessKeyPair", s3.Name, "s3 slot");
            StorageTestAssert.True(s3.AcceptedKinds.SequenceEqual(new SecretKindEnum[] { SecretKindEnum.AccessKeyPair }), "s3 kind");
            List<SecretSlotDescriptor> azure = StorageTestbed.Connector(StorageProviderEnum.AzureBlob).Descriptor.SecretSlots;
            StorageTestAssert.True(azure.Any(s => s.Name == "accessKey" && s.AcceptedKinds.Contains(SecretKindEnum.ApiToken)), "azure accessKey");
            StorageTestAssert.True(azure.Any(s => s.Name == "connectionString" && s.AcceptedKinds.Contains(SecretKindEnum.ConnectionString)), "azure connectionString");
            SecretSlotDescriptor gcs = StorageTestbed.Connector(StorageProviderEnum.GoogleCloud).Descriptor.SecretSlots.Single();
            StorageTestAssert.True(gcs.Name == "serviceAccountJson" && gcs.Required && gcs.AcceptedKinds.Contains(SecretKindEnum.ServiceAccountJson), "gcs slot");
            return Task.CompletedTask;
        }

        private static Task ToolSetShared(CancellationToken token)
        {
            List<IConnectorTool> reference = StorageTestbed.Connector(StorageProviderEnum.AmazonS3).Tools.ToList();
            StorageTestAssert.True(reference.Select(t => t.Descriptor.Name).SequenceEqual(_ToolNames), "tool names and order");
            foreach (StorageProviderEnum provider in _Providers)
            {
                List<IConnectorTool> tools = StorageTestbed.Connector(provider).Tools.ToList();
                for (int i = 0; i < tools.Count; i++)
                {
                    StorageTestAssert.Equal(reference[i].Descriptor.Name, tools[i].Descriptor.Name, "name " + i);
                    StorageTestAssert.Equal(reference[i].Descriptor.InputSchemaJson, tools[i].Descriptor.InputSchemaJson, "schema " + tools[i].Descriptor.Name);
                    StorageTestAssert.Equal(reference[i].Descriptor.BaseEffect, tools[i].Descriptor.BaseEffect, "base effect " + tools[i].Descriptor.Name);
                    StorageTestAssert.True(tools[i].Descriptor.Name.StartsWith("storage_", StringComparison.Ordinal), "family prefix");
                }
            }
            Dictionary<string, EffectEnum> baseEffects = reference.ToDictionary(t => t.Descriptor.Name, t => t.Descriptor.BaseEffect);
            StorageTestAssert.Equal(EffectEnum.Read, baseEffects["storage_list"], "list base");
            StorageTestAssert.Equal(EffectEnum.Read, baseEffects["storage_stats"], "stats base");
            StorageTestAssert.Equal(EffectEnum.Write, baseEffects["storage_presign_url"], "presign base");
            StorageTestAssert.Equal(EffectEnum.Write, baseEffects["storage_write"], "write base");
            StorageTestAssert.Equal(EffectEnum.Write, baseEffects["storage_copy"], "copy base");
            StorageTestAssert.Equal(EffectEnum.Destructive, baseEffects["storage_delete"], "delete base");
            return Task.CompletedTask;
        }

        private static Task SchemasParse(CancellationToken token)
        {
            foreach (IConnectorTool tool in StorageTestbed.Connector(StorageProviderEnum.GoogleCloud).Tools)
            {
                using (JsonDocument doc = JsonDocument.Parse(tool.Descriptor.InputSchemaJson))
                {
                    JsonElement root = doc.RootElement;
                    StorageTestAssert.Equal(JsonValueKind.Object, root.ValueKind, tool.Descriptor.Name + " root");
                    StorageTestAssert.Equal("object", root.GetProperty("type").GetString(), tool.Descriptor.Name + " type");
                    StorageTestAssert.Equal(JsonValueKind.Object, root.GetProperty("properties").ValueKind, tool.Descriptor.Name + " properties");
                    StorageTestAssert.True(!root.GetProperty("additionalProperties").GetBoolean(), tool.Descriptor.Name + " closed");
                    if (root.TryGetProperty("required", out JsonElement required))
                    {
                        foreach (JsonElement r in required.EnumerateArray())
                            StorageTestAssert.True(root.GetProperty("properties").TryGetProperty(r.GetString()!, out JsonElement _), tool.Descriptor.Name + " required property " + r.GetString() + " is declared");
                    }
                }
                StorageTestAssert.True(tool.Descriptor.Description.Length > 40, tool.Descriptor.Name + " has an LLM description");
            }
            return Task.CompletedTask;
        }

        private static Task KeyPolicyValid(CancellationToken token)
        {
            StorageTestAssert.Equal("reports/a.txt", StorageKeyPolicy.ValidateKey("reports/a.txt", "reports/"), "inside restriction");
            StorageTestAssert.Equal("a.txt", StorageKeyPolicy.ValidateKey("a.txt", ""), "no restriction");
            StorageTestAssert.Equal("dir/.hidden", StorageKeyPolicy.ValidateKey("dir/.hidden", null), "dot file");
            StorageTestAssert.Equal("dir/a..b", StorageKeyPolicy.ValidateKey("dir/a..b", null), "double dot inside name");
            StorageTestAssert.Equal("reports/", StorageKeyPolicy.ResolvePrefix(null, "reports/"), "empty prefix becomes restriction");
            StorageTestAssert.Equal("reports/2026/", StorageKeyPolicy.ResolvePrefix("reports/2026/", "reports/"), "narrower prefix");
            StorageTestAssert.Equal(String.Empty, StorageKeyPolicy.ResolvePrefix("", ""), "whole bucket");
            StorageTestAssert.True(StorageKeyPolicy.IsWithinRestriction("anything", ""), "empty restriction allows all");
            return Task.CompletedTask;
        }

        private static Task KeyPolicyRestriction(CancellationToken token)
        {
            string[] deniedKeys = new string[] { "other/a.txt", "report/a.txt", "Reports/a.txt", "reportsX/a.txt" };
            foreach (string key in deniedKeys) ExpectCode("permission_denied", () => StorageKeyPolicy.ValidateKey(key, "reports/"), "key " + key);
            ExpectCode("permission_denied", () => StorageKeyPolicy.ResolvePrefix("rep", "reports/"), "prefix shorter than restriction");
            ExpectCode("permission_denied", () => StorageKeyPolicy.ResolvePrefix("logs/", "reports/"), "sibling prefix");
            ExpectCode("permission_denied", () => StorageKeyPolicy.ResolvePrefix("reports", "reports/"), "prefix without the trailing slash");
            return Task.CompletedTask;
        }

        private static Task KeyPolicyMalformed(CancellationToken token)
        {
            string[] bad = new string[] { "reports/../secret.txt", "reports/./a.txt", "/reports/a.txt", "reports\\a.txt", "reports/a\u0000.txt", "reports/a\n.txt", "..", "reports/.." };
            foreach (string key in bad) ExpectCode("invalid_argument", () => StorageKeyPolicy.ValidateKey(key, "reports/"), "malformed key " + key.Replace("\n", "\\n").Replace("\u0000", "\\0"));
            ExpectCode("invalid_argument", () => StorageKeyPolicy.ValidateKey("", null), "empty key");
            ExpectCode("invalid_argument", () => StorageKeyPolicy.ValidateKey(new string('a', 1025), null), "too long");
            ExpectCode("invalid_argument", () => StorageKeyPolicy.ResolvePrefix("reports/../", "reports/"), "dot-dot prefix");
            return Task.CompletedTask;
        }

        private static async Task ListDeepPaging(CancellationToken token)
        {
            List<string> keys = Enumerable.Range(0, 1000).Select(i => "logs/" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + ".txt").ToList();
            List<string> seen = new List<string>();
            string? cursor = null;
            int pages = 0;
            do
            {
                StorageListPager pager = new StorageListPager { MaxScanObjects = 100, MaxSkipObjects = 1000, MaxResults = 60 };
                string? startAfter = StorageContinuationToken.Decode(cursor, "logs/");
                StorageListResult page = await pager.PageAsync(Source(keys), "logs/", null, startAfter, Info, token).ConfigureAwait(false);
                seen.AddRange(page.Entries.Select(e => e.Key));
                StorageTestAssert.True(page.Scanned <= 100, "page " + pages + " scanned within maxScanObjects: " + page.Scanned);
                cursor = page.ContinuationToken;
                pages++;
            }
            while (cursor != null && pages < 50);
            StorageTestAssert.True(seen.Count == 1000 && seen.Distinct().Count() == 1000 && seen.SequenceEqual(keys), "every key once and in order, got " + seen.Count + " in " + pages + " pages");

            List<string> nested = new List<string>();
            for (int f = 0; f < 5; f++)
                for (int i = 0; i < 40; i++) nested.Add("data/f" + f + "/" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            List<string> folders = new List<string>();
            cursor = null;
            pages = 0;
            do
            {
                StorageListPager pager = new StorageListPager { MaxScanObjects = 100, MaxSkipObjects = 1000, MaxResults = 2 };
                StorageListResult page = await pager.PageAsync(Source(nested), "data/", "/", StorageContinuationToken.Decode(cursor, "data/"), Info, token).ConfigureAwait(false);
                folders.AddRange(page.Entries.Select(e => e.Key));
                cursor = page.ContinuationToken;
                pages++;
            }
            while (cursor != null && pages < 20);
            StorageTestAssert.True(folders.SequenceEqual(new[] { "data/f0/", "data/f1/", "data/f2/", "data/f3/", "data/f4/" }), "folder pages skip the contents of the last folder: " + String.Join(",", folders));
        }

        private static async Task ListSkipBudget(CancellationToken token)
        {
            List<string> keys = Enumerable.Range(0, 500).Select(i => "k/" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)).ToList();
            StorageListPager pager = new StorageListPager { MaxScanObjects = 100, MaxSkipObjects = 200, MaxResults = 50 };
            StorageListResult ok = await pager.PageAsync(Source(keys), "k/", null, "k/0199", Info, token).ConfigureAwait(false);
            StorageTestAssert.True(ok.Skipped == 200 && ok.Entries.Count == 50 && ok.Entries[0].Key == "k/0200", "skips exactly up to the budget");
            try
            {
                await pager.PageAsync(Source(keys), "k/", null, "k/0300", Info, token).ConfigureAwait(false);
                throw new InvalidOperationException("Assertion failed: expected scan_limit past the skip budget.");
            }
            catch (ConnectorException e) when (e.Code == "scan_limit")
            {
                StorageTestAssert.True(e.Message.Contains("longer, more specific prefix", StringComparison.Ordinal), "message suggests a narrower prefix");
            }
        }

        private static async IAsyncEnumerable<BlobMetadata> Source(List<string> keys)
        {
            foreach (string key in keys)
            {
                await Task.Yield();
                yield return new BlobMetadata { Key = key, ContentLength = 10 };
            }
        }

        private static StorageObjectInfo Info(BlobMetadata md)
        {
            return new StorageObjectInfo { Key = md.Key, Size = md.ContentLength };
        }

        private static Task ContinuationToken(CancellationToken token)
        {
            string encoded = StorageContinuationToken.Encode("data/nested/");
            StorageTestAssert.Equal("data/nested/", StorageContinuationToken.Decode(encoded, "data/"), "round trip");
            StorageTestAssert.True(StorageContinuationToken.Decode(null, "data/") == null, "null is first page");
            StorageTestAssert.True(encoded.IndexOfAny(new char[] { '+', '/', '=' }) < 0, "url-safe");
            ExpectCode("invalid_argument", () => StorageContinuationToken.Decode(encoded, "logs/"), "bound to prefix");
            ExpectCode("invalid_argument", () => StorageContinuationToken.Decode("not a token!", ""), "garbage");
            ExpectCode("invalid_argument", () => StorageContinuationToken.Decode(Convert.ToBase64String(Encoding.UTF8.GetBytes("other:x")), ""), "foreign token");
            return Task.CompletedTask;
        }

        private static Task ContentDetection(CancellationToken token)
        {
            string[] textual = new string[] { "text/plain", "text/csv; charset=utf-8", "application/json", "application/ld+json", "image/svg+xml", "application/x-yaml", "APPLICATION/XML" };
            foreach (string t in textual) StorageTestAssert.True(StorageContent.IsTextualContentType(t), t + " is textual");
            string[] binary = new string[] { "application/octet-stream", "image/png", "application/pdf", "", "application/gzip" };
            foreach (string t in binary) StorageTestAssert.True(!StorageContent.IsTextualContentType(t), t + " is binary");

            StorageTestAssert.True(StorageContent.TryDecodeUtf8(Encoding.UTF8.GetBytes("héllo"), -1, false, out string text, out int used) && text == "héllo" && used == 6, "utf-8");
            byte[] binaryBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            StorageTestAssert.True(!StorageContent.TryDecodeUtf8(binaryBytes, binaryBytes.Length, false, out _, out _), "binary is not text");
            byte[] cut = Encoding.UTF8.GetBytes("ab\u00e9");
            StorageTestAssert.True(!StorageContent.TryDecodeUtf8(cut, cut.Length - 1, false, out _, out _), "cut sequence fails strictly");
            StorageTestAssert.True(StorageContent.TryDecodeUtf8(cut, cut.Length - 1, true, out string tail, out int tailUsed) && tail == "ab" && tailUsed == 2, "cut sequence tolerated at tail");
            StorageTestAssert.Equal("application/json", StorageContent.GuessContentType("a/b.json", "x"), "guess json");
            StorageTestAssert.Equal("x", StorageContent.GuessContentType("a.dir/noext", "x"), "no extension");
            StorageTestAssert.Equal("1.5 KiB", StorageContent.FormatBytes(1536), "format bytes");
            return Task.CompletedTask;
        }

        private static Task ClassifyReadTools(CancellationToken token)
        {
            StorageConnectorBase connector = StorageTestbed.Connector(StorageProviderEnum.AmazonS3);
            ConnectorInvocationContext context = StorageTestbed.Context(Offline(StorageProviderEnum.AmazonS3));
            string[] calls = new string[]
            {
                "storage_list|{}",
                "storage_list|{\"prefix\":\"logs/\",\"delimiter\":\"/\"}",
                "storage_get_metadata|{\"key\":\"logs/app.log\"}",
                "storage_read|{\"key\":\"logs/app.log\",\"offset\":10}",
                "storage_stats|{\"prefix\":\"logs/\"}"
            };
            foreach (string call in calls)
            {
                string[] parts = call.Split('|', 2);
                EffectClassification c = StorageTestbed.Classify(connector, context, parts[0], parts[1]);
                StorageTestAssert.Equal(EffectEnum.Read, c.Effect, call);
                StorageTestAssert.True(!c.Rejected, call + " not rejected");
                StorageTestAssert.True(c.ApprovalSummary.Contains("acme-assets", StringComparison.Ordinal), call + " names bucket");
            }
            StorageTestAssert.True(StorageTestbed.Classify(connector, context, "storage_read", "{\"key\":\"logs/app.log\"}").ApprovalSummary.Contains("'logs/app.log'", StringComparison.Ordinal), "read names key");
            return Task.CompletedTask;
        }

        private static Task ClassifyWrite(CancellationToken token)
        {
            StorageConnectorBase connector = StorageTestbed.Connector(StorageProviderEnum.AzureBlob);
            ConnectorInvocationContext context = StorageTestbed.Context(Offline(StorageProviderEnum.AzureBlob));
            EffectClassification create = StorageTestbed.Classify(connector, context, "storage_write", "{\"key\":\"out/report.md\",\"content\":\"hello\"}");
            StorageTestAssert.Equal(EffectEnum.Write, create.Effect, "create is Write");
            StorageTestAssert.True(create.ApprovalSummary.Contains("'out/report.md'", StringComparison.Ordinal) && create.ApprovalSummary.Contains("container 'acme-assets'", StringComparison.Ordinal), "summary names key and container: " + create.ApprovalSummary);
            StorageTestAssert.True(create.ApprovalSummary.Contains("5 bytes", StringComparison.Ordinal), "summary names size");
            EffectClassification noOverwrite = StorageTestbed.Classify(connector, context, "storage_write", "{\"key\":\"out/report.md\",\"content\":\"x\",\"overwrite\":false}");
            StorageTestAssert.Equal(EffectEnum.Write, noOverwrite.Effect, "overwrite=false is Write");
            EffectClassification overwrite = StorageTestbed.Classify(connector, context, "storage_write", "{\"key\":\"out/report.md\",\"content\":\"x\",\"overwrite\":true}");
            StorageTestAssert.Equal(EffectEnum.Destructive, overwrite.Effect, "overwrite=true is Destructive");
            StorageTestAssert.True(overwrite.ApprovalSummary.Contains("replacing", StringComparison.Ordinal), "summary says replacing");
            EffectClassification badEncoding = StorageTestbed.Classify(connector, context, "storage_write", "{\"key\":\"out/a\",\"content\":\"x\",\"encoding\":\"hex\"}");
            StorageTestAssert.True(badEncoding.Rejected, "unknown encoding rejected (fail closed)");
            return Task.CompletedTask;
        }

        private static Task ClassifyCopy(CancellationToken token)
        {
            StorageConnectorBase connector = StorageTestbed.Connector(StorageProviderEnum.GoogleCloud);
            ConnectorInvocationContext context = StorageTestbed.Context(Offline(StorageProviderEnum.GoogleCloud));
            EffectClassification copy = StorageTestbed.Classify(connector, context, "storage_copy", "{\"sourceKey\":\"a/x.txt\",\"destinationKey\":\"b/x.txt\"}");
            StorageTestAssert.Equal(EffectEnum.Write, copy.Effect, "copy is Write");
            StorageTestAssert.True(copy.ApprovalSummary.Contains("'a/x.txt'", StringComparison.Ordinal) && copy.ApprovalSummary.Contains("'b/x.txt'", StringComparison.Ordinal), "names both keys");
            EffectClassification overwrite = StorageTestbed.Classify(connector, context, "storage_copy", "{\"sourceKey\":\"a/x.txt\",\"destinationKey\":\"b/x.txt\",\"overwrite\":true}");
            StorageTestAssert.Equal(EffectEnum.Destructive, overwrite.Effect, "copy with overwrite is Destructive");
            return Task.CompletedTask;
        }

        private static Task ClassifyDelete(CancellationToken token)
        {
            StorageConnectorBase connector = StorageTestbed.Connector(StorageProviderEnum.AmazonS3);
            ConnectorInvocationContext context = StorageTestbed.Context(Offline(StorageProviderEnum.AmazonS3));
            context.BindingAlias = "assets";
            EffectClassification delete = StorageTestbed.Classify(connector, context, "storage_delete", "{\"key\":\"data/2026/10/export.csv\"}");
            StorageTestAssert.Equal(EffectEnum.Destructive, delete.Effect, "delete is Destructive");
            StorageTestAssert.Equal("Delete 'data/2026/10/export.csv' from bucket 'acme-assets' (assets)", delete.ApprovalSummary, "summary");
            StorageTestAssert.True(StorageTestbed.Classify(connector, context, "storage_delete", "{}").Rejected, "missing key rejected (fail closed)");
            return Task.CompletedTask;
        }

        private static Task ClassifyPresign(CancellationToken token)
        {
            StorageConnectorBase connector = StorageTestbed.Connector(StorageProviderEnum.AmazonS3);
            ConnectorInvocationContext context = StorageTestbed.Context(Offline(StorageProviderEnum.AmazonS3));
            EffectClassification get = StorageTestbed.Classify(connector, context, "storage_presign_url", "{\"key\":\"a.txt\",\"expirySeconds\":600}");
            StorageTestAssert.Equal(EffectEnum.Write, get.Effect, "GET presign is Write");
            StorageTestAssert.True(get.ApprovalSummary.Contains("600 seconds", StringComparison.Ordinal) && get.ApprovalSummary.Contains("'a.txt'", StringComparison.Ordinal), "summary has key and expiry");
            EffectClassification put = StorageTestbed.Classify(connector, context, "storage_presign_url", "{\"key\":\"a.txt\",\"method\":\"PUT\"}");
            StorageTestAssert.Equal(EffectEnum.Destructive, put.Effect, "PUT presign is Destructive");
            StorageTestAssert.True(StorageTestbed.Classify(connector, context, "storage_presign_url", "{\"key\":\"a.txt\",\"method\":\"DELETE\"}").Rejected, "unknown method rejected");
            return Task.CompletedTask;
        }

        private static Task ClassifyRejects(CancellationToken token)
        {
            Dictionary<string, string> restricted = new Dictionary<string, string> { { "prefixRestriction", "tenant-a/" } };
            StorageConnectorBase connector = StorageTestbed.Connector(StorageProviderEnum.AmazonS3);
            ConnectorInvocationContext context = StorageTestbed.Context(Offline(StorageProviderEnum.AmazonS3, restricted));
            string[] rejected = new string[]
            {
                "storage_list|{\"prefix\":\"tenant-b/\"}",
                "storage_stats|{\"prefix\":\"tenant\"}",
                "storage_get_metadata|{\"key\":\"tenant-b/a\"}",
                "storage_read|{\"key\":\"tenant-a/../tenant-b/a\"}",
                "storage_write|{\"key\":\"tenant-b/a\",\"content\":\"x\"}",
                "storage_copy|{\"sourceKey\":\"tenant-a/x\",\"destinationKey\":\"tenant-b/x\"}",
                "storage_copy|{\"sourceKey\":\"tenant-b/x\",\"destinationKey\":\"tenant-a/x\"}",
                "storage_delete|{\"key\":\"tenant-b/a\"}",
                "storage_presign_url|{\"key\":\"/tenant-a/a\"}"
            };
            foreach (string call in rejected)
            {
                string[] parts = call.Split('|', 2);
                EffectClassification c = StorageTestbed.Classify(connector, context, parts[0], parts[1]);
                StorageTestAssert.True(c.Rejected, call + " rejected");
                StorageTestAssert.Equal(EffectEnum.Destructive, c.Effect, call + " fails closed");
            }
            StorageTestAssert.True(!StorageTestbed.Classify(connector, context, "storage_list", "{}").Rejected, "default prefix allowed");
            StorageTestAssert.True(StorageTestbed.Classify(connector, context, "storage_list", "{}").ApprovalSummary.Contains("'tenant-a/'", StringComparison.Ordinal), "default prefix shown");
            return Task.CompletedTask;
        }

        private static async Task ValidateS3(CancellationToken token)
        {
            S3Connector connector = new S3Connector();
            StorageTestAssert.True((await connector.ValidateAsync(Offline(StorageProviderEnum.AmazonS3), token).ConfigureAwait(false)).IsValid, "valid");

            ConnectorResourceContext anonymous = WithSecrets(Offline(StorageProviderEnum.AmazonS3), new Dictionary<string, Dictionary<string, string>>());
            StorageTestAssert.True((await connector.ValidateAsync(anonymous, token).ConfigureAwait(false)).IsValid, "anonymous is valid");

            ConnectorResourceContext noBucket = Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "bucket", "" } });
            StorageTestAssert.True((await connector.ValidateAsync(noBucket, token).ConfigureAwait(false)).Errors.Any(e => e.Field == "bucket"), "bucket required");

            ConnectorResourceContext badEndpoint = Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "endpoint", "ftp://example.com" } });
            StorageTestAssert.True((await connector.ValidateAsync(badEndpoint, token).ConfigureAwait(false)).Errors.Any(e => e.Field == "endpoint"), "endpoint scheme");

            ConnectorResourceContext halfPair = WithSecrets(Offline(StorageProviderEnum.AmazonS3), new Dictionary<string, Dictionary<string, string>>
            {
                { "accessKeyPair", new Dictionary<string, string> { { "accessKeyId", "AKID" } } }
            });
            StorageTestAssert.True((await connector.ValidateAsync(halfPair, token).ConfigureAwait(false)).Errors.Any(e => e.Field == "accessKeyPair"), "both halves required");

            ConnectorResourceContext session = WithSecrets(Offline(StorageProviderEnum.AmazonS3), new Dictionary<string, Dictionary<string, string>>
            {
                { "accessKeyPair", new Dictionary<string, string> { { "accessKeyId", "AKID" }, { "secretAccessKey", "s" }, { "sessionToken", "t" } } }
            });
            StorageTestAssert.True((await connector.ValidateAsync(session, token).ConfigureAwait(false)).Errors.Any(e => e.Code == "not_supported"), "session token unsupported");
        }

        private static async Task ValidateAzure(CancellationToken token)
        {
            AzureBlobConnector connector = new AzureBlobConnector();
            StorageTestAssert.True((await connector.ValidateAsync(Offline(StorageProviderEnum.AzureBlob), token).ConfigureAwait(false)).IsValid, "valid with key");

            string connectionString = "DefaultEndpointsProtocol=http;AccountName=acmeassets;AccountKey=" + StorageTestbed.AzuriteKey + ";BlobEndpoint=http://127.0.0.1:9/acmeassets;";
            ConnectorResourceContext withConnectionString = WithSecrets(Offline(StorageProviderEnum.AzureBlob), new Dictionary<string, Dictionary<string, string>>
            {
                { "connectionString", new Dictionary<string, string> { { "connectionString", connectionString } } }
            });
            StorageTestAssert.True((await connector.ValidateAsync(withConnectionString, token).ConfigureAwait(false)).IsValid, "valid with connection string");

            ConnectorResourceContext both = WithSecrets(Offline(StorageProviderEnum.AzureBlob), new Dictionary<string, Dictionary<string, string>>
            {
                { "accessKey", new Dictionary<string, string> { { "token", StorageTestbed.AzuriteKey } } },
                { "connectionString", new Dictionary<string, string> { { "connectionString", connectionString } } }
            });
            StorageTestAssert.True((await connector.ValidateAsync(both, token).ConfigureAwait(false)).Errors.Any(e => e.Code == "conflict"), "both is a conflict");

            ConnectorResourceContext none = WithSecrets(Offline(StorageProviderEnum.AzureBlob), new Dictionary<string, Dictionary<string, string>>());
            StorageTestAssert.True((await connector.ValidateAsync(none, token).ConfigureAwait(false)).Errors.Any(e => e.Code == "required"), "one is required");

            ConnectorResourceContext noAccount = Offline(StorageProviderEnum.AzureBlob, new Dictionary<string, string> { { "accountName", "" } });
            StorageTestAssert.True((await connector.ValidateAsync(noAccount, token).ConfigureAwait(false)).Errors.Any(e => e.Field == "accountName"), "account name with key");

            ConnectorResourceContext badKey = WithSecrets(Offline(StorageProviderEnum.AzureBlob), new Dictionary<string, Dictionary<string, string>>
            {
                { "accessKey", new Dictionary<string, string> { { "token", "not base64 !!" } } }
            });
            StorageTestAssert.True((await connector.ValidateAsync(badKey, token).ConfigureAwait(false)).Errors.Any(e => e.Field == "accessKey"), "key must be base64");
        }

        private static async Task ValidateGcs(CancellationToken token)
        {
            GcsConnector connector = new GcsConnector();
            StorageTestAssert.True((await connector.ValidateAsync(Offline(StorageProviderEnum.GoogleCloud), token).ConfigureAwait(false)).IsValid, "valid");
            string[] badJson = new string[] { "not json", "{\"type\":\"authorized_user\",\"client_email\":\"a\",\"private_key\":\"b\"}", "{\"type\":\"service_account\"}" };
            foreach (string json in badJson)
            {
                ConnectorResourceContext bad = WithSecrets(Offline(StorageProviderEnum.GoogleCloud), new Dictionary<string, Dictionary<string, string>>
                {
                    { "serviceAccountJson", new Dictionary<string, string> { { "json", json } } }
                });
                StorageTestAssert.True((await connector.ValidateAsync(bad, token).ConfigureAwait(false)).Errors.Any(e => e.Field == "serviceAccountJson"), "rejects " + json);
            }
            ConnectorResourceContext missing = WithSecrets(Offline(StorageProviderEnum.GoogleCloud), new Dictionary<string, Dictionary<string, string>>());
            StorageTestAssert.True((await connector.ValidateAsync(missing, token).ConfigureAwait(false)).Errors.Any(e => e.Code == "required"), "key required");
        }

        private static async Task ValidatePrefixRestriction(CancellationToken token)
        {
            S3Connector connector = new S3Connector();
            foreach (string bad in new string[] { "/abs/", "a/../b/", "a\\b/" })
            {
                ConnectorResourceContext resource = Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "prefixRestriction", bad } });
                StorageTestAssert.True((await connector.ValidateAsync(resource, token).ConfigureAwait(false)).Errors.Any(e => e.Field == "prefixRestriction"), "rejects " + bad);
            }
            ConnectorResourceContext good = Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "prefixRestriction", "tenant-a/" } });
            StorageTestAssert.True((await connector.ValidateAsync(good, token).ConfigureAwait(false)).IsValid, "accepts tenant-a/");
        }

        private static Task FactoryS3Settings(CancellationToken token)
        {
            Blobject.AmazonS3.AwsSettings http = StorageClientFactory.BuildS3Settings(Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "endpoint", "http://minio.local:9000/" } }));
            StorageTestAssert.Equal("http://minio.local:9000", http.Endpoint, "endpoint authority");
            StorageTestAssert.True(!http.Ssl, "http endpoint disables TLS");
            StorageTestAssert.True(http.HasCredentials, "credentials mapped");
            StorageTestAssert.Equal("us-east-1", http.Region, "default region");

            Blobject.AmazonS3.AwsSettings bare = StorageClientFactory.BuildS3Settings(Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "endpoint", "minio.local:9000" }, { "useSsl", "true" } }));
            StorageTestAssert.True(bare.Ssl && bare.Endpoint == "https://minio.local:9000", "schemeless endpoint uses useSsl");

            Blobject.AmazonS3.AwsSettings aws = StorageClientFactory.BuildS3Settings(Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "endpoint", "" }, { "region", "eu-west-2" } }));
            StorageTestAssert.True(aws.Endpoint == null && aws.Ssl && aws.Region == "eu-west-2", "AWS defaults");

            ConnectorResourceContext anonymous = WithSecrets(Offline(StorageProviderEnum.AmazonS3), new Dictionary<string, Dictionary<string, string>>());
            StorageTestAssert.True(!StorageClientFactory.BuildS3Settings(anonymous).HasCredentials, "anonymous");
            return Task.CompletedTask;
        }

        private static Task S3AuthenticationErrors(CancellationToken token)
        {
            string sensitive = "AKIDPRIVATEEXAMPLE secretAccessKey=private-signing-material canonical-request-private";
            foreach ((string awsCode, HttpStatusCode status, string expectedCode, string guidance) in new[]
            {
                ("InvalidAccessKeyId", HttpStatusCode.Forbidden, "invalid_credentials", "access key ID"),
                ("SignatureDoesNotMatch", HttpStatusCode.Forbidden, "invalid_credentials", "signature"),
                ("ExpiredToken", HttpStatusCode.Forbidden, "invalid_credentials", "token"),
                ("RequestTimeTooSkewed", HttpStatusCode.Forbidden, "invalid_credentials", "clock"),
                ("AuthorizationHeaderMalformed", HttpStatusCode.BadRequest, "invalid_setting", "region"),
                ("PermanentRedirect", HttpStatusCode.MovedPermanently, "invalid_setting", "endpoint"),
                ("AccessDenied", HttpStatusCode.Forbidden, "permission_denied", "policies"),
                ("NoSuchBucket", HttpStatusCode.NotFound, "not_found", "Not found"),
                ("UnrecognizedUpstreamFailure", HttpStatusCode.InternalServerError, "upstream_error", "configuration")
            })
            {
                Amazon.S3.AmazonS3Exception aws = new Amazon.S3.AmazonS3Exception(sensitive) { ErrorCode = awsCode, StatusCode = status };
                Exception wrapped = new InvalidOperationException("SDK operation failed", aws);
                ConnectorException mapped = StorageErrors.Map(wrapped, "acme-assets", null);
                StorageTestAssert.Equal(expectedCode, mapped.Code, awsCode + " retains the specific AWS failure category through a wrapper");
                StorageTestAssert.True(mapped.Message.Contains(guidance, StringComparison.OrdinalIgnoreCase), awsCode + " gives actionable fixed guidance");
                StorageTestAssert.True(!mapped.Message.Contains("AKIDPRIVATEEXAMPLE", StringComparison.Ordinal) && !mapped.Message.Contains("private-signing-material", StringComparison.Ordinal) && !mapped.Message.Contains("canonical-request-private", StringComparison.Ordinal), awsCode + " does not expose SDK request/signing material");
            }
            return Task.CompletedTask;
        }

        private static async Task S3RegionalSigning(CancellationToken token)
        {
            ConnectorResourceContext resource = Offline(StorageProviderEnum.AmazonS3, new Dictionary<string, string> { { "endpoint", "" }, { "region", "us-east-2" } });
            Blobject.AmazonS3.AwsSettings settings = StorageClientFactory.BuildS3Settings(resource);
            StorageTestAssert.True(settings.Endpoint == null && settings.Ssl, "Amazon S3 uses its regional HTTPS endpoint");
            StorageTestAssert.Equal("us-east-2", settings.AwsRegion.SystemName, "configured region reaches AWS SDK region resolution");
            StorageTestAssert.Equal("AKIDEXAMPLE", settings.AccessKey, "configured access key reaches the S3 client");
            StorageTestAssert.Equal("secret-example", settings.SecretKey, "configured secret reaches the S3 client");
            using (S3Presigner signer = new S3Presigner(settings))
            {
                Uri signed = new Uri(await signer.PresignAsync("folder/report.txt", StoragePresignMethodEnum.Get, TimeSpan.FromMinutes(5), token).ConfigureAwait(false));
                string query = Uri.UnescapeDataString(signed.Query);
                StorageTestAssert.Equal("https", signed.Scheme, "TLS without a custom endpoint");
                StorageTestAssert.Equal("acme-assets.s3.us-east-2.amazonaws.com", signed.Host, "bucket regional AWS host");
                StorageTestAssert.Equal("/folder/report.txt", signed.AbsolutePath, "virtual hosted addressing keeps the bucket out of the object path");
                StorageTestAssert.True(query.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", StringComparison.Ordinal), "SigV4 signing");
                StorageTestAssert.True(query.Contains("X-Amz-Credential=AKIDEXAMPLE/", StringComparison.Ordinal) && query.Contains("/us-east-2/s3/aws4_request", StringComparison.Ordinal), "credential scope uses the requested bucket region");
                StorageTestAssert.True(!query.Contains("secret-example", StringComparison.Ordinal), "signing never publishes the secret key");
            }
        }

        private static Task FactoryEndpoints(CancellationToken token)
        {
            StorageTestAssert.Equal("http://127.0.0.1:4443/storage/v1/", StorageClientFactory.NormalizeGcsEndpoint("http://127.0.0.1:4443"), "gcs append");
            StorageTestAssert.Equal("http://127.0.0.1:4443/storage/v1/", StorageClientFactory.NormalizeGcsEndpoint("http://127.0.0.1:4443/storage/v1/"), "gcs keep");
            StorageTestAssert.True(StorageClientFactory.NormalizeGcsEndpoint("") == null, "gcs none");
            ResourceSettings azure = new ResourceSettings(new Dictionary<string, string> { { "accountName", "acme" } });
            StorageTestAssert.Equal("https://acme.blob.core.windows.net", StorageClientFactory.ResolveAzureEndpoint(azure), "azure default");
            ResourceSettings azurite = new ResourceSettings(new Dictionary<string, string> { { "accountName", "devstoreaccount1" }, { "endpoint", "http://127.0.0.1:10000/devstoreaccount1/" } });
            StorageTestAssert.Equal("http://127.0.0.1:10000/devstoreaccount1", StorageClientFactory.ResolveAzureEndpoint(azurite), "azure custom");
            ExpectCode("invalid_setting", () => StorageClientFactory.ParseEndpoint("ftp://x", true, "endpoint"), "ftp rejected");
            return Task.CompletedTask;
        }

        private static Task FactorySessions(CancellationToken token)
        {
            foreach (StorageProviderEnum provider in _Providers)
            {
                using (StorageSession session = StorageClientFactory.CreateSession(provider, Offline(provider)))
                {
                    StorageTestAssert.Equal(provider, session.Provider, "provider");
                    StorageTestAssert.Equal("acme-assets", session.ContainerName, "container");
                    StorageTestAssert.True(session.Presigner != null, provider + " has a presigner");
                    StorageTestAssert.Equal(provider == StorageProviderEnum.GoogleCloud, session.BuffersWholeObjectOnRead, "buffering flag");
                }
            }
            ConnectorResourceContext anonymous = WithSecrets(Offline(StorageProviderEnum.AmazonS3), new Dictionary<string, Dictionary<string, string>>());
            using (StorageSession session = StorageClientFactory.CreateSession(StorageProviderEnum.AmazonS3, anonymous))
            {
                StorageTestAssert.True(session.Presigner == null && session.PresignUnavailableReason.Length > 0, "anonymous S3 cannot presign");
            }
            ConnectorResourceContext badGcs = WithSecrets(Offline(StorageProviderEnum.GoogleCloud), new Dictionary<string, Dictionary<string, string>>
            {
                { "serviceAccountJson", new Dictionary<string, string> { { "json", "{\"type\":\"service_account\"}" } } }
            });
            ExpectCode("invalid_credentials", () => StorageClientFactory.CreateSession(StorageProviderEnum.GoogleCloud, badGcs).Dispose(), "bad gcs key");
            return Task.CompletedTask;
        }

        private static Task OptionsClamp(CancellationToken token)
        {
            StorageOptions defaults = StorageOptions.FromSettings(new ResourceSettings(null));
            StorageTestAssert.True(defaults.PrefixRestriction == "" && defaults.MaxObjectReadBytes == 1048576 && defaults.MaxListResults == 1000 && defaults.MaxScanObjects == 100000, "defaults");
            StorageOptions clamped = StorageOptions.FromSettings(new ResourceSettings(new Dictionary<string, string>
            {
                { "maxObjectReadBytes", "10" }, { "maxListResults", "999999" }, { "maxScanObjects", "1" }, { "prefixRestriction", " a/ " }
            }));
            StorageTestAssert.True(clamped.MaxObjectReadBytes == 1024 && clamped.MaxListResults == 10000 && clamped.MaxScanObjects == 100 && clamped.PrefixRestriction == "a/", "clamped");
            return Task.CompletedTask;
        }

        private static async Task ExecutePermissionDenied(CancellationToken token)
        {
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                foreach (StorageProviderEnum provider in _Providers)
                {
                    ConnectorResourceContext resource = Offline(provider, new Dictionary<string, string> { { "prefixRestriction", "tenant-a/" } });
                    resource.Sessions = pool;
                    StorageConnectorBase connector = StorageTestbed.Connector(provider);
                    ConnectorInvocationContext context = StorageTestbed.Context(resource);
                    string[] calls = new string[]
                    {
                        "storage_list|{\"prefix\":\"tenant-b/\"}",
                        "storage_stats|{\"prefix\":\"other/\"}",
                        "storage_get_metadata|{\"key\":\"tenant-b/a\"}",
                        "storage_read|{\"key\":\"tenant-b/a\"}",
                        "storage_write|{\"key\":\"tenant-b/a\",\"content\":\"x\"}",
                        "storage_copy|{\"sourceKey\":\"tenant-a/x\",\"destinationKey\":\"tenant-b/x\"}",
                        "storage_delete|{\"key\":\"tenant-b/a\"}",
                        "storage_presign_url|{\"key\":\"tenant-b/a\"}"
                    };
                    foreach (string call in calls)
                    {
                        string[] parts = call.Split('|', 2);
                        ToolResult result = await StorageTestbed.InvokeAsync(connector, context, parts[0], parts[1], token).ConfigureAwait(false);
                        StorageTestAssert.Error(result, "permission_denied", provider + " " + call);
                    }
                    ToolResult malformed = await StorageTestbed.InvokeAsync(connector, context, "storage_read", "{\"key\":\"tenant-a/../x\"}", token).ConfigureAwait(false);
                    StorageTestAssert.Error(malformed, "invalid_argument", provider + " dot segments");
                }
            }
        }

        private static async Task ExecuteTimeout(CancellationToken token)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (NoPoolSessionPool pool = new NoPoolSessionPool())
                {
                    foreach (StorageProviderEnum provider in _Providers)
                    {
                        string endpointKey = provider == StorageProviderEnum.GoogleCloud ? "customEndpoint" : "endpoint";
                        string endpoint = "http://127.0.0.1:" + port + (provider == StorageProviderEnum.AzureBlob ? "/acmeassets" : String.Empty);
                        ConnectorResourceContext resource = Offline(provider, new Dictionary<string, string> { { endpointKey, endpoint } });
                        resource.Sessions = pool;
                        ConnectorInvocationContext context = StorageTestbed.Context(resource);
                        context.Timeout = TimeSpan.FromSeconds(1);
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        ToolResult result = await StorageTestbed.InvokeAsync(StorageTestbed.Connector(provider), context, "storage_get_metadata", "{\"key\":\"a.txt\"}", token).ConfigureAwait(false);
                        StorageTestAssert.Error(result, "timeout", provider + " times out");
                        StorageTestAssert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), provider + " honors the timeout promptly (" + stopwatch.Elapsed.TotalSeconds + " s)");
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task ExecuteCancelled(CancellationToken token)
        {
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                ConnectorResourceContext resource = Offline(StorageProviderEnum.AmazonS3);
                resource.Sessions = pool;
                bool threw = false;
                try
                {
                    await StorageTestbed.InvokeAsync(new S3Connector(), StorageTestbed.Context(resource), "storage_list", "{}", cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
                StorageTestAssert.True(threw, "cancellation propagates");
            }
        }

        private static async Task TestConnectionInvalid(CancellationToken token)
        {
            ConnectorResourceContext none = WithSecrets(Offline(StorageProviderEnum.AzureBlob), new Dictionary<string, Dictionary<string, string>>());
            ConnectorHealthResult health = await new AzureBlobConnector().TestConnectionAsync(none, token).ConfigureAwait(false);
            StorageTestAssert.True(!health.Success && health.ErrorCode == "invalid_configuration", "invalid configuration: " + health.Message);
        }

        private static void ExpectCode(string code, Action action, string message)
        {
            try
            {
                action();
            }
            catch (ConnectorException e)
            {
                StorageTestAssert.Equal(code, e.Code, message);
                return;
            }
            throw new InvalidOperationException("Assertion failed: " + message + " (expected ConnectorException " + code + ")");
        }

        #endregion
    }
}
