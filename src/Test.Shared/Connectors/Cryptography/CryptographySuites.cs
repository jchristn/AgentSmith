namespace Test.Shared.Connectors.Cryptography
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Cryptography;
    using Touchstone.Core;

    /// <summary>Deterministic published-vector, binary round-trip, and safety cases for local cryptography.</summary>
    public static class CryptographySuites
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Cryptography cases, requiring no external services.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            string id = "connectors.cryptography";
            return new List<TestSuiteDescriptor> { new TestSuiteDescriptor(id, "Administrator-key cryptography, published vectors, authenticated envelopes and bounded output", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(id, "descriptor", "Four Read tools and optional Generic slots", Descriptor),
                new TestCaseDescriptor(id, "gcm-vector", "NIST GCM zero-key vector and tampered authentication tag", GcmVector),
                new TestCaseDescriptor(id, "cbc-vector", "NIST CBC first block and administrator-bound IV with PKCS7", CbcVector),
                new TestCaseDescriptor(id, "roundtrips", "Unicode and binary round trips in separate and combined formats", Roundtrips),
                new TestCaseDescriptor(id, "hash-vectors", "SHA-256/384/512 published abc digests and verification", HashVectors),
                new TestCaseDescriptor(id, "limits", "Input/output bounds fail atomically without partial data", Limits),
                new TestCaseDescriptor(id, "invalid", "Reject keys, envelopes, encodings and malformed inputs without leaking secrets", Invalid),
                new TestCaseDescriptor(id, "health", "Local self-test with key and hash-only configurations", Health),
                new TestCaseDescriptor(id, "cancellation", "Cancelled calls stop before processing", Cancellation)
            }) };
        }

        private static Task Descriptor(CancellationToken token)
        {
            CryptographyConnector connector = new CryptographyConnector();
            Assert(connector.Descriptor.Type == "cryptography" && connector.Descriptor.IconKey == "shield", "catalog metadata");
            Assert(connector.Tools.Count == 4 && connector.Tools.All(t => t.Descriptor.BaseEffect == EffectEnum.Read), "read-only effects");
            Assert(connector.Descriptor.SecretSlots.All(s => !s.Required && s.AcceptedKinds.Contains(SecretKindEnum.Generic)), "optional generic secrets");
            Assert(connector.Descriptor.DefaultInstructions.Contains("truncated") && connector.Descriptor.DefaultInstructions.Contains("Never"), "instructions loaded");
            return Task.CompletedTask;
        }

        private static async Task GcmVector(CancellationToken token)
        {
            ConnectorInvocationContext context = Context("AES-GCM", new byte[16]);
            CryptoResponse response = await Success(context, "decrypt", new CryptoRequest { Data = "0388dace60b6a392f328c2b971b2fe78", InputEncoding = "hex", OutputEncoding = "hex", Nonce = "000000000000000000000000", Tag = "ab6e47d42cec13bdf53a67b21257bddf" }, token).ConfigureAwait(false);
            Assert(response.Data == new string('0', 32), "NIST GCM plaintext");
            ToolResult bad = await Call(context, "decrypt", new CryptoRequest { Data = "0388dace60b6a392f328c2b971b2fe78", InputEncoding = "hex", Nonce = "000000000000000000000000", Tag = "ab6e47d42cec13bdf53a67b21257bdde" }, token).ConfigureAwait(false);
            Assert(bad.IsError && bad.StructuredJson == null && bad.ErrorCode == "cryptographic_failure", "tamper returns no plaintext");
        }

        private static async Task CbcVector(CancellationToken token)
        {
            byte[] key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
            byte[] iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
            byte[] plaintext = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172a");
            byte[] ciphertext;
            using (Aes aes = Aes.Create()) { aes.Key = key; ciphertext = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7); }
            Assert(Convert.ToHexString(ciphertext.AsSpan(0, 16)) == "7649ABAC8119B246CEE98E9B12E9197D", "NIST CBC block");
            ConnectorInvocationContext context = Context("AES-CBC", key, iv);
            CryptoResponse response = await Success(context, "decrypt", new CryptoRequest { Data = Convert.ToBase64String(ciphertext), OutputEncoding = "hex" }, token).ConfigureAwait(false);
            Assert(response.Data == Convert.ToHexString(plaintext).ToLowerInvariant() && response.Warning != null, "raw CBC decrypt with bound IV");
            ToolResult malformed = await Call(context, "decrypt", new CryptoRequest { Data = "AQID" }, token).ConfigureAwait(false);
            Assert(malformed.IsError && malformed.StructuredJson == null, "incomplete CBC block fails safely");
            byte[] paddingError = new byte[16];
            ToolResult badPadding = await Call(context, "decrypt", new CryptoRequest { Data = Convert.ToBase64String(paddingError) }, token).ConfigureAwait(false);
            Assert(badPadding.IsError && badPadding.StructuredJson == null, "invalid PKCS7 padding fails safely");
        }

        private static async Task Roundtrips(CancellationToken token)
        {
            foreach (string algorithm in new string[] { "AES-GCM", "AES-CBC" })
            foreach (string format in new string[] { "combined", "separate" })
            foreach (int keyLength in new int[] { 16, 24, 32 })
            {
                ConnectorInvocationContext context = Context(algorithm, RandomNumberGenerator.GetBytes(keyLength));
                byte[] binary = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
                CryptoResponse encrypted = await Success(context, "encrypt", new CryptoRequest { Data = Convert.ToBase64String(binary), Format = format }, token).ConfigureAwait(false);
                CryptoResponse decoded = await Success(context, "decrypt", new CryptoRequest { Data = encrypted.Data, Format = format, Nonce = encrypted.Nonce, Tag = encrypted.Tag }, token).ConfigureAwait(false);
                Assert(decoded.Data == Convert.ToBase64String(binary), "all byte values preserved");
                CryptoResponse first = await Success(context, "encrypt", new CryptoRequest { Data = "Hello 世界 🌍", InputEncoding = "utf8", Format = format }, token).ConfigureAwait(false);
                CryptoResponse second = await Success(context, "encrypt", new CryptoRequest { Data = "Hello 世界 🌍", InputEncoding = "utf8", Format = format }, token).ConfigureAwait(false);
                Assert(first.Data != second.Data || first.Nonce != second.Nonce, "fresh random nonce/IV");
                CryptoResponse text = await Success(context, "decrypt", new CryptoRequest { Data = first.Data, Nonce = first.Nonce, Tag = first.Tag, Format = format, OutputEncoding = "utf8" }, token).ConfigureAwait(false);
                Assert(text.Data == "Hello 世界 🌍", "Unicode preserved");
            }
            ConnectorInvocationContext gcm = Context("AES-GCM", new byte[32]);
            CryptoResponse authenticated = await Success(gcm, "encrypt", new CryptoRequest { Data = "AQID", AssociatedData = "BAUG" }, token).ConfigureAwait(false);
            ToolResult mismatch = await Call(gcm, "decrypt", new CryptoRequest { Data = authenticated.Data, Nonce = authenticated.Nonce, Tag = authenticated.Tag, AssociatedData = "BAUH" }, token).ConfigureAwait(false);
            Assert(mismatch.IsError, "AAD mismatch rejected");
            CryptoResponse matched = await Success(gcm, "decrypt", new CryptoRequest { Data = authenticated.Data, Nonce = authenticated.Nonce, Tag = authenticated.Tag, AssociatedData = "BAUG" }, token).ConfigureAwait(false);
            Assert(matched.Data == "AQID", "matching AAD accepted");
        }

        private static async Task HashVectors(CancellationToken token)
        {
            Dictionary<string, string> vectors = new Dictionary<string, string>
            {
                { "SHA256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" },
                { "SHA384", "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7" },
                { "SHA512", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f" }
            };
            ConnectorInvocationContext context = Context("AES-GCM", null);
            foreach (KeyValuePair<string, string> vector in vectors)
            {
                CryptoRequest request = new CryptoRequest { Data = "abc", InputEncoding = "utf8", OutputEncoding = "hex", HashAlgorithm = vector.Key };
                CryptoResponse hash = await Success(context, "hash", request, token).ConfigureAwait(false);
                Assert(hash.Data == vector.Value, "published digest " + vector.Key);
                request.ExpectedHash = vector.Value;
                Assert((await Success(context, "verify_hash", request, token).ConfigureAwait(false)).Matches == true, "digest match");
                request.ExpectedHash = new string('0', vector.Value.Length);
                Assert((await Success(context, "verify_hash", request, token).ConfigureAwait(false)).Matches == false, "digest mismatch");
            }
        }

        private static async Task Limits(CancellationToken token)
        {
            ConnectorInvocationContext context = Context("AES-GCM", new byte[32]);
            context.Resource.Settings = new ResourceSettings(new Dictionary<string, string> { { "maxInputBytes", "3" } });
            Assert((await Call(context, "encrypt", new CryptoRequest { Data = "AQIDBA==" }, token).ConfigureAwait(false)).IsError, "decoded input bound");
            Assert((await Call(context, "hash", new CryptoRequest { Data = new string(' ', 100) }, token).ConfigureAwait(false)).IsError, "preallocation encoded input bound");
            context.Resource.Settings = new ResourceSettings(null);
            context.Limits.MaxBytes = 1024;
            ToolResult large = await Call(context, "encrypt", new CryptoRequest { Data = Convert.ToBase64String(new byte[1024]) }, token).ConfigureAwait(false);
            Assert(large.IsError && large.StructuredJson == null && !large.Truncated, "output fails atomically");
        }

        private static async Task Invalid(CancellationToken token)
        {
            ConnectorInvocationContext context = Context("AES-GCM", new byte[32]);
            foreach (CryptoRequest request in new CryptoRequest[]
            {
                new CryptoRequest { Data = "bad!" },
                new CryptoRequest { Data = "00", InputEncoding = "hex", Nonce = "00", Tag = new string('0',32) },
                new CryptoRequest { Data = "AQID", Format = "guess" },
                new CryptoRequest { Data = "abc", InputEncoding = "utf8" },
                new CryptoRequest { Data = "AQID", Format = "combined", Nonce = "AQID" }
            }) Assert((await Call(context, "decrypt", request, token).ConfigureAwait(false)).IsError, "invalid envelope rejected");
            Assert((await new CryptographyTool("encrypt").ExecuteAsync(context, new ToolArguments("{\"data\":\"AQID\",\"key\":\"DO-NOT-EXPOSE\"}"), token).ConfigureAwait(false)).IsError, "tool key arguments prohibited");
            Assert((await new CryptographyTool("encrypt").ExecuteAsync(context, new ToolArguments("{\"data\":null}"), token).ConfigureAwait(false)).IsError, "null input rejected");
            ConnectorInvocationContext missing = Context("AES-GCM", null);
            Assert((await Call(missing, "encrypt", new CryptoRequest { Data = "AQID" }, token).ConfigureAwait(false)).IsError, "key required for cipher");
            foreach (int size in new int[] { 0, 15, 17, 33 })
            {
                ConnectorInvocationContext invalid = Context("AES-GCM", new byte[size]);
                Assert(!(await new CryptographyConnector().ValidateAsync(invalid.Resource, token).ConfigureAwait(false)).IsValid, "invalid key size " + size);
            }
            ConnectorInvocationContext hex = Context("AES-GCM", null);
            hex.Resource.Settings = new ResourceSettings(new Dictionary<string, string> { { "keyEncoding", "hex" } });
            hex.Resource.Secrets = new SecretAccessor(new Dictionary<string, Dictionary<string, string>> { { "encryptionKey", new Dictionary<string, string> { { "value", new string('a', 64) } } } });
            Assert((await new CryptographyConnector().ValidateAsync(hex.Resource, token).ConfigureAwait(false)).IsValid, "hex vault key accepted");
        }

        private static async Task Health(CancellationToken token)
        {
            foreach (string algorithm in new string[] { "AES-GCM", "AES-CBC" })
                Assert((await new CryptographyConnector().TestConnectionAsync(Context(algorithm, new byte[32]).Resource, token).ConfigureAwait(false)).Success, "cipher self-test " + algorithm);
            Assert((await new CryptographyConnector().TestConnectionAsync(Context("AES-GCM", null).Resource, token).ConfigureAwait(false)).Success, "hash-only self-test");
        }

        private static async Task Cancellation(CancellationToken token)
        {
            using (CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                source.Cancel();
                try { await Call(Context("AES-GCM", null), "hash", new CryptoRequest { Data = "AQID" }, source.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                throw new InvalidOperationException("Cancellation must propagate.");
            }
        }

        private static ConnectorInvocationContext Context(string algorithm, byte[]? key, byte[]? iv = null)
        {
            Dictionary<string, Dictionary<string, string>> slots = new Dictionary<string, Dictionary<string, string>>();
            if (key != null) slots.Add("encryptionKey", new Dictionary<string, string> { { "value", Convert.ToBase64String(key) } });
            if (iv != null) slots.Add("initializationVector", new Dictionary<string, string> { { "value", Convert.ToBase64String(iv) } });
            return new ConnectorInvocationContext { Resource = new ConnectorResourceContext { Settings = new ResourceSettings(new Dictionary<string, string> { { "algorithm", algorithm } }), Secrets = new SecretAccessor(slots) } };
        }

        private static Task<ToolResult> Call(ConnectorInvocationContext context, string operation, CryptoRequest request, CancellationToken token)
        {
            string json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            return new CryptographyTool(operation).ExecuteAsync(context, new ToolArguments(json), token);
        }

        private static async Task<CryptoResponse> Success(ConnectorInvocationContext context, string operation, CryptoRequest request, CancellationToken token)
        {
            ToolResult result = await Call(context, operation, request, token).ConfigureAwait(false);
            Assert(!result.IsError, "expected success: " + result.ErrorCode + " " + result.Text);
            return JsonSerializer.Deserialize<CryptoResponse>(result.StructuredJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
