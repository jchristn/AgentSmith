namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Cryptography;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Secrets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>Vault-backed cryptography through the normal authenticated resource and invocation pipeline.</summary>
    public static class CryptographyIntegrationSuite
    {
        /// <summary>Build provider-specific integration tests.</summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return TestEnvironment.Providers().Select(provider =>
            {
                string id = "cryptography-integration-" + provider.ToString().ToLowerInvariant();
                return new TestSuiteDescriptor(id, "Vault-backed cryptography (" + provider + ")", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "aes-bundle-vault", "AES bundle fields are encrypted, validated, and accepted by cryptography resources", ct => AesBundleAsync(provider, ct)),
                    new TestCaseDescriptor(id, "vault-roundtrip", "Encrypted binary BLOBs round-trip through a read-only bound resource without passing or returning the key", ct => RoundtripAsync(provider, ct))
                });
            }).ToList();
        }

        private static async Task AesBundleAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                Secret secret = await rig.SecretAsync("aes-bundle", SecretKindEnum.AesKey, new Dictionary<string, string> { { "key", key }, { "salt", Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) }, { "iv", Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) } }, token).ConfigureAwait(false);
                Assert.True(secret.Kind == SecretKindEnum.AesKey && secret.Fingerprint == null, "AES bundle retains its kind without a key-content hint");
                Assert.True(SecretFieldRules.PrimaryField(SecretKindEnum.AesKey) == null, "AES key contents never provide a last-four hint");
                Assert.True(!SecretFieldRules.Accepts(SecretKindEnum.AesKey, "password") && SecretFieldRules.Required(SecretKindEnum.AesKey).SequenceEqual(new[] { "key" }), "only supported AES fields accepted and key is required");
                Resource resource = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new
                {
                    name = "aes-bundle-crypto", connectorType = "cryptography",
                    settings = new Dictionary<string, object> { { "algorithm", "AES-CBC" } },
                    secretBindings = new[] { new { slotName = "encryptionKey", secretId = secret.Id } }
                }, 201, token).ConfigureAwait(false);
                ResourceTestResponse health = await HttpTestHelpers.SendAsync<ResourceTestResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/" + resource.Id + "/test", new { }, 200, token).ConfigureAwait(false);
                Assert.True(health.Success, "AES key bundle resolves and self-tests through resource pipeline");
            }
        }

        private static async Task RoundtripAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                Secret secret = await rig.SecretAsync("blob-key", SecretKindEnum.Generic, new Dictionary<string, string> { { "value", key } }, token).ConfigureAwait(false);
                Resource resource = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new
                {
                    name = "blob-crypto", connectorType = "cryptography",
                    settings = new Dictionary<string, object> { { "algorithm", "AES-GCM" }, { "keyEncoding", "base64" } },
                    secretBindings = new[] { new { slotName = "encryptionKey", secretId = secret.Id } }
                }, 201, token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                ResourceTestResponse health = await HttpTestHelpers.SendAsync<ResourceTestResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/" + resource.Id + "/test", new { }, 200, token).ConfigureAwait(false);
                Assert.True(health.Success, "vault-backed local health probe succeeds");
                byte[] bytes = Encoding.UTF8.GetBytes("Encrypted binary BLOB \u2603\0payload");
                string input = Convert.ToBase64String(bytes);
                ToolInvocationOutcome encrypted = await rig.InvokeAsync("crypto_encrypt", "blob-crypto", new { data = input, format = "combined" }, token).ConfigureAwait(false);
                Assert.True(encrypted.Result != null && !encrypted.Result.IsError && encrypted.Effect == Hugo.Connectors.Abstractions.EffectEnum.Read, "encryption runs through the read-only binding");
                CryptoResponse cipher = JsonSerializer.Deserialize<CryptoResponse>(encrypted.Result!.StructuredJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                ToolInvocationOutcome decrypted = await rig.InvokeAsync("crypto_decrypt", "blob-crypto", new { data = cipher.Data, format = "combined" }, token).ConfigureAwait(false);
                Assert.True(decrypted.Result != null && !decrypted.Result.IsError, "decryption succeeds using the bound vault key");
                CryptoResponse plaintext = JsonSerializer.Deserialize<CryptoResponse>(decrypted.Result!.StructuredJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                Assert.Equal(input, plaintext.Data, "every binary byte is preserved");
                Assert.True(!encrypted.Result.Text.Contains(key, StringComparison.Ordinal) && !decrypted.Result.Text.Contains(key, StringComparison.Ordinal), "tool outputs never contain the configured key");
                string expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                ToolInvocationOutcome verified = await rig.InvokeAsync("crypto_verify_hash", "blob-crypto", new { data = plaintext.Data, outputEncoding = "hex", expectedHash = expected }, token).ConfigureAwait(false);
                CryptoResponse digest = JsonSerializer.Deserialize<CryptoResponse>(verified.Result!.StructuredJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                Assert.True(digest.Matches == true, "recovered BLOB hash matches");
                ToolInvocationOutcome rejected = await rig.InvokeAsync("crypto_decrypt", "blob-crypto", new { data = cipher.Data, format = "combined", key = "not-allowed" }, token).ConfigureAwait(false);
                Assert.True(rejected.Decision != InvocationDecisionEnum.Allowed || rejected.Result?.IsError == true, "callers cannot provide replacement key material in arguments");
            }
        }
    }
}
