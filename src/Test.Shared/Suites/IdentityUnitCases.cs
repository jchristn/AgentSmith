namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Security;
    using Hugo.Server.Services;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// Identity unit cases that need no server.
    /// </summary>
    public static class IdentityUnitCases
    {
        /// <summary>
        /// Tampered and expired tokens are rejected.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static Task TokenTamperAsync(CancellationToken token)
        {
            TokenService tokens = new TokenService("signing-key", "hugo", 60);
            TokenPayload payload = new TokenPayload();
            payload.SessionId = "ses_1";
            payload.TokenId = "tok";
            payload.TenantId = "ten_a";
            payload.ExpiresUtc = DateTime.UtcNow.AddMinutes(5);
            string issued = tokens.Issue(payload);
            Assert.True(tokens.Validate(issued) != null, "Valid token rejected.");

            char[] chars = issued.ToCharArray();
            int index = chars.Length / 2;
            chars[index] = chars[index] == 'A' ? 'B' : 'A';
            Assert.True(tokens.Validate(new string(chars)) == null, "Tampered token accepted.");
            Assert.True(new TokenService("other-key", "hugo", 60).Validate(issued) == null, "Token accepted with the wrong key.");

            payload.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            Assert.True(tokens.Validate(tokens.Issue(payload)) == null, "Expired token accepted.");
            Assert.True(tokens.Validate("not-a-token") == null, "Garbage accepted.");
            byte[] firstKey = RandomNumberGenerator.GetBytes(32);
            byte[] secondKey = RandomNumberGenerator.GetBytes(32);
            SecretCipher firstCipher = new SecretCipher(firstKey, 1);
            TokenService master = new TokenService(firstCipher, "hugo", 60, "signing-key");
            payload.ExpiresUtc = DateTime.UtcNow.AddMinutes(5);
            string masterToken = master.Issue(payload);
            Assert.True(masterToken != master.Issue(payload), "Session nonces must be random.");
            Assert.True(master.Validate(masterToken) != null, "Master-key token rejected.");
            Assert.True(master.Validate(issued) != null, "Pre-upgrade token rejected.");
            Assert.True(new TokenService(firstCipher, "other-issuer", 60).Validate(masterToken) == null, "Wrong issuer accepted.");
            Assert.True(new TokenService(new SecretCipher(secondKey, 1), "hugo", 60).Validate(masterToken) == null, "Wrong master key accepted.");
            TokenService rotated = new TokenService(new SecretCipher(secondKey, 2, firstKey, 1), "hugo", 60);
            Assert.True(rotated.Validate(masterToken) != null, "Previous master key token rejected during rotation.");
            string rotatedToken = rotated.Issue(payload);
            Assert.Equal(2, SecretCipher.ReadKeyVersion(rotatedToken.Substring(6)), "New sessions use current version");
            Assert.True(master.Validate(rotatedToken) == null, "Unknown master key version accepted.");
            char[] frame = masterToken.ToCharArray();
            frame[frame.Length / 2] = frame[frame.Length / 2] == 'A' ? 'B' : 'A';
            Assert.True(master.Validate(new string(frame)) == null, "Tampered master-key token accepted.");
            string wrongPurpose = "hugo1." + firstCipher.Encrypt(Hugo.Core.Constants.KeyPurposeSecrets, "{}", "hugo");
            Assert.True(master.Validate(wrongPurpose) == null, "Secret ciphertext accepted as a session.");
            payload.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            Assert.True(master.Validate(master.Issue(payload)) == null, "Expired master-key token accepted.");
            Assert.True(new TokenService(firstCipher, "hugo", 60).Validate(issued) == null, "Legacy fallback accepted when disabled.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Password hashing: PBKDF2 format, per-password salt, wrong passwords, legacy SHA-256 verification, and rehash detection.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static Task PasswordHashingAsync(CancellationToken token)
        {
            string first = PasswordHasher.Hash("correct horse");
            string second = PasswordHasher.Hash("correct horse");
            Assert.True(first.StartsWith("pbkdf2-sha256$600000$", StringComparison.Ordinal), "Default format and iterations: " + first);
            string[] parts = first.Split('$');
            Assert.Equal(4, parts.Length, "Encoded parts");
            Assert.Equal(16, Convert.FromBase64String(parts[2]).Length, "Salt bytes");
            Assert.Equal(32, Convert.FromBase64String(parts[3]).Length, "Hash bytes");
            Assert.True(first.Length <= 256, "Encoded hash fits the 256-character column: " + first.Length);
            Assert.True(first != second, "Two hashes of one password share a salt.");
            Assert.True(PasswordHasher.Verify("correct horse", first), "Correct password rejected.");
            Assert.True(PasswordHasher.Verify("correct horse", second), "Correct password rejected (second salt).");
            Assert.True(!PasswordHasher.Verify("correct horsf", first), "Wrong password accepted.");
            Assert.True(!PasswordHasher.Verify("", first), "Empty password accepted.");
            Assert.True(!PasswordHasher.Verify(null, first), "Null password accepted.");
            Assert.True(!PasswordHasher.NeedsRehash(first, PasswordHasher.DefaultIterations), "Current hash flagged for rehash.");

            string low = PasswordHasher.Hash("correct horse", 10000);
            Assert.True(low.StartsWith("pbkdf2-sha256$10000$", StringComparison.Ordinal), "Explicit iterations: " + low);
            Assert.True(PasswordHasher.Verify("correct horse", low), "Low-iteration hash rejected.");
            Assert.True(PasswordHasher.NeedsRehash(low, PasswordHasher.DefaultIterations), "Hash with fewer iterations not flagged.");
            Assert.True(!PasswordHasher.NeedsRehash(first, 10000), "Hash with more iterations flagged for a downgrade.");

            string legacy = PasswordHasher.LegacySha256("correct horse");
            Assert.Equal(64, legacy.Length, "Legacy length");
            Assert.True(PasswordHasher.IsLegacy(legacy), "Legacy hash not detected.");
            Assert.True(PasswordHasher.Verify("correct horse", legacy), "Legacy hash rejected.");
            Assert.True(PasswordHasher.Verify("correct horse", legacy.ToUpperInvariant()), "Uppercase legacy hash rejected.");
            Assert.True(!PasswordHasher.Verify("wrong horse", legacy), "Wrong password accepted against a legacy hash.");
            Assert.True(PasswordHasher.NeedsRehash(legacy, PasswordHasher.DefaultIterations), "Legacy hash not flagged for rehash.");

            foreach (string malformed in new[] { "pbkdf2-sha256$", "pbkdf2-sha256$abc$AAAA$AAAA", "pbkdf2-sha256$1000$!!!$AAAA", "pbkdf2-sha256$1000$$", "plain-text-password", "correct horse" })
            {
                Assert.True(!PasswordHasher.Verify("correct horse", malformed), "Malformed hash accepted: " + malformed);
            }
            Assert.True(!PasswordHasher.VerifyDummy("correct horse"), "Dummy verification returned true.");

            bool threw = false;
            try
            {
                PasswordHasher.Hash("x", 10);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Assert.True(threw, "Too few iterations accepted.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Comparison is constant time: equal-length comparisons that differ at the first or last byte take about as long,
        /// and a dummy verification for an unknown user takes about as long as a real one.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static Task PasswordTimingAsync(CancellationToken token)
        {
            string a = new string('a', 4096);
            string earlyDiff = "b" + new string('a', 4095);
            string lateDiff = new string('a', 4095) + "b";
            Assert.True(PasswordHasher.FixedTimeEquals(a, new string('a', 4096)), "Equal strings compared unequal.");
            Assert.True(!PasswordHasher.FixedTimeEquals(a, earlyDiff) && !PasswordHasher.FixedTimeEquals(a, lateDiff), "Different strings compared equal.");
            Assert.True(!PasswordHasher.FixedTimeEquals(a, null) && !PasswordHasher.FixedTimeEquals(null, a), "Null compared equal.");

            for (int i = 0; i < 2000; i++) PasswordHasher.FixedTimeEquals(a, earlyDiff);
            long early = MedianTicks(() => PasswordHasher.FixedTimeEquals(a, earlyDiff), 2000);
            long late = MedianTicks(() => PasswordHasher.FixedTimeEquals(a, lateDiff), 2000);
            double ratio = (double)Math.Max(early, late) / Math.Max(1, Math.Min(early, late));
            Assert.True(ratio < 3.0, "Comparison time depends on where the strings differ (early " + early + " ticks, late " + late + " ticks).");

            string stored = PasswordHasher.Hash("correct horse");
            long real = MedianTicks(() => PasswordHasher.Verify("wrong horse", stored), 5);
            long dummy = MedianTicks(() => PasswordHasher.VerifyDummy("wrong horse"), 5);
            double verifyRatio = (double)Math.Max(real, dummy) / Math.Max(1, Math.Min(real, dummy));
            Assert.True(verifyRatio < 3.0, "Unknown-user verification time differs from a real verification (real " + real + " ticks, dummy " + dummy + " ticks).");
            return Task.CompletedTask;
        }

        /// <summary>
        /// RBAC evaluator semantics.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static Task RbacMatrixAsync(CancellationToken token)
        {
            PermissionGrant permitAll = Grant(new[] { "All" }, new[] { "All" }, PermissionTypeEnum.Permit, ResourceScopeEnum.Tenant, null, true);
            PermissionGrant denyAgentDelete = Grant(new[] { "AgentProfile" }, new[] { "Delete" }, PermissionTypeEnum.Deny, ResourceScopeEnum.Tenant, null, true);
            List<PermissionGrant> grants = new List<PermissionGrant> { permitAll, denyAgentDelete };
            Assert.Equal(AuthorizationResultEnum.DeniedExplicit, PermissionEvaluator.Evaluate(grants, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Delete, "agp_1").Result, "Deny beats permit");
            Assert.True(PermissionEvaluator.Evaluate(grants, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, "agp_1").Permitted, "Permit All reads");

            PermissionGrant resourceScoped = Grant(new[] { "AgentProfile" }, new[] { "All" }, PermissionTypeEnum.Permit, ResourceScopeEnum.Resource, "agp_x", true);
            List<PermissionGrant> scoped = new List<PermissionGrant> { resourceScoped };
            Assert.True(PermissionEvaluator.Evaluate(scoped, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, "agp_x").Permitted, "Resource grant on its resource");
            Assert.Equal(AuthorizationResultEnum.DeniedImplicit, PermissionEvaluator.Evaluate(scoped, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, "agp_y").Result, "Resource grant on another resource");
            Assert.Equal(AuthorizationResultEnum.DeniedImplicit, PermissionEvaluator.Evaluate(scoped, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, null).Result, "Resource grant at tenant level");

            PermissionGrant noInherit = Grant(new[] { "AgentProfile" }, new[] { "Read" }, PermissionTypeEnum.Permit, ResourceScopeEnum.Tenant, null, false);
            List<PermissionGrant> tenantOnly = new List<PermissionGrant> { noInherit };
            Assert.True(PermissionEvaluator.Evaluate(tenantOnly, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, null).Permitted, "Tenant-level read");
            Assert.True(!PermissionEvaluator.Evaluate(tenantOnly, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, "agp_1").Permitted, "Non-inheriting grant applied to a resource");

            PermissionGrant write = Grant(new[] { "Resource" }, new[] { "Write" }, PermissionTypeEnum.Permit, ResourceScopeEnum.Tenant, null, true);
            List<PermissionGrant> writes = new List<PermissionGrant> { write };
            Assert.True(PermissionEvaluator.Evaluate(writes, ResourceTypeEnum.Resource, OperationTypeEnum.Create, null).Permitted, "Write covers Create");
            Assert.True(PermissionEvaluator.Evaluate(writes, ResourceTypeEnum.Resource, OperationTypeEnum.Delete, null).Permitted, "Write covers Delete");
            Assert.True(!PermissionEvaluator.Evaluate(writes, ResourceTypeEnum.Resource, OperationTypeEnum.Read, null).Permitted, "Write does not cover Read");
            Assert.True(!PermissionEvaluator.Evaluate(writes, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Create, null).Permitted, "Write is limited to its resource type");
            Assert.Equal(AuthorizationResultEnum.DeniedImplicit, PermissionEvaluator.Evaluate(new List<PermissionGrant>(), ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, null).Result, "No grants");

            // Reveal is never implied by All or Write; only an explicit Reveal grant covers it (D-011).
            Assert.True(!PermissionEvaluator.Evaluate(grants, ResourceTypeEnum.Secret, OperationTypeEnum.Reveal, "sec_1").Permitted, "Permit All implied Reveal");
            List<PermissionGrant> secretWriter = new List<PermissionGrant> { Grant(new[] { "Secret" }, new[] { "Read", "Write" }, PermissionTypeEnum.Permit, ResourceScopeEnum.Tenant, null, true) };
            Assert.True(!PermissionEvaluator.Evaluate(secretWriter, ResourceTypeEnum.Secret, OperationTypeEnum.Reveal, "sec_1").Permitted, "Write implied Reveal");
            List<PermissionGrant> revealer = new List<PermissionGrant> { Grant(new[] { "Secret" }, new[] { "Reveal" }, PermissionTypeEnum.Permit, ResourceScopeEnum.Tenant, null, true) };
            Assert.True(PermissionEvaluator.Evaluate(revealer, ResourceTypeEnum.Secret, OperationTypeEnum.Reveal, "sec_1").Permitted, "Explicit Reveal grant");
            Assert.True(!PermissionEvaluator.Evaluate(revealer, ResourceTypeEnum.Secret, OperationTypeEnum.Read, "sec_1").Permitted, "Reveal implied Read");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Secret cipher round trip and tamper detection.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static Task SecretCipherAsync(CancellationToken token)
        {
            SecretCipher cipher = new SecretCipher(RandomNumberGenerator.GetBytes(32), 1);
            string encrypted = cipher.EncryptCredential("secret_value_123", "crd_1");
            Assert.Equal("secret_value_123", cipher.DecryptCredential(encrypted, "crd_1"), "Round trip");
            Assert.True(!encrypted.Contains("secret_value"), "Plaintext visible in ciphertext.");
            bool rejected = false;
            try
            {
                cipher.DecryptCredential(encrypted, "crd_2");
            }
            catch (CryptographicException)
            {
                rejected = true;
            }
            Assert.True(rejected, "Associated data mismatch was not detected.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Redaction rules.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static Task RedactionAsync(CancellationToken token)
        {
            foreach (string header in new[] { "Authorization", "x-api-key", "X-Secret-Key", "x-token", "Cookie", "x-custom-token", "x-password", "x-signature", "X-Password", "Set-Cookie", "x-access-key" })
            {
                Assert.True(RequestHistoryCaptureService.IsSecretHeader(header), header + " should be redacted.");
            }
            Assert.True(!RequestHistoryCaptureService.IsSecretHeader("Content-Type"), "Content-Type redacted.");
            Assert.True(RequestHistoryCaptureService.IsSecretRoute("/v1.0/api/tenants/ten_1/secrets/sec_1/value"), "Secret route not detected.");
            Assert.True(RequestHistoryCaptureService.IsSecretRoute("/v1.0/token"), "Token route not detected.");
            Assert.True(RequestHistoryCaptureService.IsSecretRoute("/v1.0/api/tenants/ten_1/credentials"), "Credential route not detected.");
            Assert.True(RequestHistoryCaptureService.IsSecretRoute("/v1.0/api/users/me/password"), "Password route not detected.");
            Assert.True(!RequestHistoryCaptureService.IsSecretRoute("/v1.0/api/tenants/ten_1/roles"), "Role route redacted.");
            Assert.True(RequestHistoryCaptureService.IsHistoryRoute("/v1.0/api/request-history"), "History list route not detected.");
            Assert.True(RequestHistoryCaptureService.IsHistoryRoute("/v1.0/api/request-history/req_1"), "History detail route not detected.");
            Assert.True(!RequestHistoryCaptureService.IsHistoryRoute("/v1.0/api/health"), "Health route treated as history.");
            Assert.True(!RequestHistoryCaptureService.IsHistoryRoute(null), "Null path treated as history.");
            return Task.CompletedTask;
        }

        private static long MedianTicks(Func<bool> action, int samples)
        {
            List<long> ticks = new List<long>();
            for (int i = 0; i < samples; i++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                action();
                watch.Stop();
                ticks.Add(watch.ElapsedTicks);
            }
            ticks.Sort();
            return ticks[ticks.Count / 2];
        }

        private static PermissionGrant Grant(string[] types, string[] operations, PermissionTypeEnum type, ResourceScopeEnum scope, string? resourceId, bool inherits)
        {
            PermissionGrant grant = new PermissionGrant();
            grant.ResourceTypes = new List<string>(types);
            grant.Operations = new List<string>(operations);
            grant.PermissionType = type;
            grant.Scope = scope;
            grant.ResourceId = resourceId;
            grant.InheritsToChildren = inherits;
            grant.Source = "test";
            return grant;
        }
    }
}
