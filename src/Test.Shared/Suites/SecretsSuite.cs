namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Secrets;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Secrets vault suites (P2.12): cipher, key loading and canary, import parsers, scrubber, service, routes, reveal,
    /// master key rotation, and tenant scoping of secrets.
    /// </summary>
    public static class SecretsSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            List<TestCaseDescriptor> unit = new List<TestCaseDescriptor>();
            unit.Add(new TestCaseDescriptor("secrets-unit", "cipher", "AES-256-GCM round trip, wrong AAD, tampering, previous key, version byte, purpose separation", CipherAsync));
            unit.Add(new TestCaseDescriptor("secrets-unit", "key-loader", "Master key sources, development warnings, generated key file, and malformed keys", KeyLoaderAsync));
            unit.Add(new TestCaseDescriptor("secrets-unit", "scrubber", "Known values in plain, base64, and URL-encoded form, and secret patterns, are redacted", ScrubberAsync));
            unit.Add(new TestCaseDescriptor("secrets-unit", "import-ssh", "SSH private keys in OpenSSH and PEM formats yield the ssh-keygen fingerprint", ImportSshAsync));
            unit.Add(new TestCaseDescriptor("secrets-unit", "import-other", "GCP service account JSON, kubeconfig contexts, and PEM certificates are described", ImportOtherAsync));
            unit.Add(new TestCaseDescriptor("secrets-unit", "field-rules", "Secret kinds accept their documented fields", FieldRulesAsync));
            suites.Add(new TestSuiteDescriptor("secrets-unit", "Secrets unit", unit));

            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "secrets-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "service", "Secret service: validation, mask placeholder, field removal, fingerprints, referrers, 409 on delete (" + provider + ")", ct => ServiceAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "canary", "The master key canary admits the right key and refuses the wrong one (" + provider + ")", ct => CanaryAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "rotation-service", "Rotation re-encrypts secret values and credential secrets under the new key version (" + provider + ")", ct => RotationServiceAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "routes", "Secret routes never return values and audit every change (" + provider + ")", ct => RoutesAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "reveal", "Reveal requires the permission, a reason, and recent re-authentication; it is audited and never in request history (" + provider + ")", ct => RevealAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "reveal-rate-limit", "Reveal is rate limited (" + provider + ")", ct => RevealRateLimitAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "rotation-route", "Rotation across restarts: previous key reads, rotate route, new key alone, wrong key refused (" + provider + ")", ct => RotationRouteAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Secrets vault (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Unit-Cases

        private static Task CipherAsync(CancellationToken token)
        {
            byte[] oldKey = RandomNumberGenerator.GetBytes(32);
            byte[] newKey = RandomNumberGenerator.GetBytes(32);
            SecretCipher v1 = new SecretCipher(oldKey, 1);
            string aad = SecretCipher.SecretFieldAssociatedData("ten_a", "sec_1", "password");
            Assert.Equal("ten_a|sec_1|password", aad, "Associated data format");

            string frame = v1.Encrypt(Constants.KeyPurposeSecrets, "hunter2-plaintext", aad);
            Assert.Equal("hunter2-plaintext", v1.Decrypt(Constants.KeyPurposeSecrets, frame, aad), "Round trip");
            byte[] raw = Convert.FromBase64String(frame);
            Assert.Equal(1, (int)raw[0], "Format version byte");
            Assert.Equal(1, (int)raw[1], "Key version byte");
            Assert.Equal(2 + 12 + 16 + "hunter2-plaintext".Length, raw.Length, "Frame length");
            Assert.True(frame != v1.Encrypt(Constants.KeyPurposeSecrets, "hunter2-plaintext", aad), "Nonce reuse: two encryptions are identical.");

            Fails(() => v1.Decrypt(Constants.KeyPurposeSecrets, frame, SecretCipher.SecretFieldAssociatedData("ten_b", "sec_1", "password")), "Other tenant AAD");
            Fails(() => v1.Decrypt(Constants.KeyPurposeSecrets, frame, SecretCipher.SecretFieldAssociatedData("ten_a", "sec_2", "password")), "Other secret AAD");
            Fails(() => v1.Decrypt(Constants.KeyPurposeSecrets, frame, SecretCipher.SecretFieldAssociatedData("ten_a", "sec_1", "username")), "Other field AAD");
            Fails(() => v1.Decrypt(Constants.KeyPurposeCredentials, frame, aad), "Other purpose subkey");

            byte[] tampered = (byte[])raw.Clone();
            tampered[20] ^= 0x01;
            Fails(() => v1.Decrypt(Constants.KeyPurposeSecrets, Convert.ToBase64String(tampered), aad), "Tampered tag");
            byte[] tamperedBody = (byte[])raw.Clone();
            tamperedBody[tamperedBody.Length - 1] ^= 0x01;
            Fails(() => v1.Decrypt(Constants.KeyPurposeSecrets, Convert.ToBase64String(tamperedBody), aad), "Tampered ciphertext");
            byte[] truncated = raw.Take(10).ToArray();
            Fails(() => v1.Decrypt(Constants.KeyPurposeSecrets, Convert.ToBase64String(truncated), aad), "Truncated frame");

            SecretCipher v2 = new SecretCipher(newKey, 2, oldKey, 1);
            Assert.Equal("hunter2-plaintext", v2.Decrypt(Constants.KeyPurposeSecrets, frame, aad), "Previous key decrypts");
            string reencrypted = v2.Reencrypt(Constants.KeyPurposeSecrets, frame, aad);
            Assert.Equal(2, SecretCipher.ReadKeyVersion(reencrypted), "New writes use the current version");
            Assert.Equal(2, SecretCipher.ReadKeyVersion(v2.Encrypt(Constants.KeyPurposeSecrets, "x", aad)), "Current version");
            Fails(() => v1.Decrypt(Constants.KeyPurposeSecrets, reencrypted, aad), "Old cipher cannot read version 2");
            SecretCipher v2Only = new SecretCipher(newKey, 2);
            Fails(() => v2Only.Decrypt(Constants.KeyPurposeSecrets, frame, aad), "Version 1 frame without the previous key");
            SecretCipher wrong = new SecretCipher(RandomNumberGenerator.GetBytes(32), 1);
            Fails(() => wrong.Decrypt(Constants.KeyPurposeSecrets, frame, aad), "Wrong key");

            bool shortKey = false;
            try
            {
                new SecretCipher(new byte[16], 1);
            }
            catch (ArgumentException)
            {
                shortKey = true;
            }
            Assert.True(shortKey, "A 16-byte master key was accepted.");

            string credential = v2.EncryptCredential("secret_abc", "crd_1");
            Assert.Equal("secret_abc", v2.DecryptCredential(credential, "crd_1"), "Credential round trip");
            Fails(() => v2.DecryptCredential(credential, "crd_2"), "Credential AAD");
            return Task.CompletedTask;
        }

        private static Task KeyLoaderAsync(CancellationToken token)
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            string previous = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            List<string> warnings = new List<string>();

            MasterKeyOptions env = new MasterKeyOptions();
            env.EnvironmentKey = key;
            env.SettingsKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            MasterKeyLoadResult fromEnv = MasterKeyLoader.Load(env, warnings.Add);
            Assert.Equal(MasterKeySourceEnum.Environment, fromEnv.Source, "Environment wins");
            Assert.Equal(0, warnings.Count, "No warning for the environment key");

            MasterKeyOptions settings = new MasterKeyOptions();
            settings.SettingsKey = key;
            MasterKeyLoadResult fromSettings = MasterKeyLoader.Load(settings, warnings.Add);
            Assert.Equal(MasterKeySourceEnum.SettingsFile, fromSettings.Source, "Settings key");
            Assert.True(warnings.Count == 1 && warnings[0].Contains("development only", StringComparison.Ordinal), "Settings key warns loudly.");

            string file = Path.Combine(Path.GetTempPath(), "hugo-test", Guid.NewGuid().ToString("N"), "master.key");
            MasterKeyOptions keyFile = new MasterKeyOptions();
            keyFile.KeyFile = file;
            MasterKeyLoadResult generated = MasterKeyLoader.Load(keyFile, warnings.Add);
            Assert.Equal(MasterKeySourceEnum.GeneratedKeyFile, generated.Source, "Generated key file");
            Assert.True(File.Exists(file), "Key file not written.");
            string frame = generated.Cipher.Encrypt(Constants.KeyPurposeSecrets, "x", "y");
            MasterKeyLoadResult reused = MasterKeyLoader.Load(keyFile, warnings.Add);
            Assert.Equal(MasterKeySourceEnum.KeyFile, reused.Source, "Existing key file");
            Assert.Equal("x", reused.Cipher.Decrypt(Constants.KeyPurposeSecrets, frame, "y"), "Key file reused across loads");
            Assert.Equal(3, warnings.Count, "Every development source warned");
            File.Delete(file);

            MasterKeyOptions rotation = new MasterKeyOptions();
            rotation.EnvironmentKey = key;
            rotation.EnvironmentPreviousKey = previous;
            rotation.KeyVersion = 2;
            MasterKeyLoadResult rotating = MasterKeyLoader.Load(rotation, warnings.Add);
            Assert.True(rotating.HasPreviousKey && rotating.Cipher.HasKeyVersion(1) && rotating.Cipher.HasKeyVersion(2), "Previous key loaded as version 1.");

            MasterKeyException? missing = Throws(() => MasterKeyLoader.Load(new MasterKeyOptions(), null));
            Assert.True(missing != null && missing.Message.Contains("HUGO_MASTER_KEY", StringComparison.Ordinal), "Missing key.");
            MasterKeyOptions shortKey = new MasterKeyOptions();
            shortKey.EnvironmentKey = Convert.ToBase64String(new byte[20]);
            Assert.True(Throws(() => MasterKeyLoader.Load(shortKey, null)) != null, "Short key accepted.");
            MasterKeyOptions notBase64 = new MasterKeyOptions();
            notBase64.EnvironmentKey = "not base64 !!";
            Assert.True(Throws(() => MasterKeyLoader.Load(notBase64, null)) != null, "Malformed key accepted.");
            MasterKeyOptions noVersion = new MasterKeyOptions();
            noVersion.EnvironmentKey = key;
            noVersion.EnvironmentPreviousKey = previous;
            noVersion.KeyVersion = 1;
            Assert.True(Throws(() => MasterKeyLoader.Load(noVersion, null)) != null, "A previous key without a previous version was accepted.");
            return Task.CompletedTask;
        }

        private static Task ScrubberAsync(CancellationToken token)
        {
            SecretScrubber scrubber = new SecretScrubber();
            scrubber.AddKnownValue("tr0ub4dor&3");
            scrubber.AddKnownValue("abc");
            Assert.Equal(1 * 3, scrubber.KnownValueCount, "Plain, base64, and URL-encoded forms registered; short values ignored");
            string text = "login with tr0ub4dor&3, encoded " + Convert.ToBase64String(Encoding.UTF8.GetBytes("tr0ub4dor&3")) + " or " + Uri.EscapeDataString("tr0ub4dor&3") + " and abc";
            string scrubbed = scrubber.Scrub(text, out int redactions)!;
            Assert.True(!scrubbed.Contains("tr0ub4dor", StringComparison.Ordinal) && !scrubbed.Contains("dHIwdWI0ZG9yJjM", StringComparison.Ordinal), "Known value leaked: " + scrubbed);
            Assert.Equal(3, redactions, "Known value redactions");
            Assert.True(scrubbed.Contains("and abc", StringComparison.Ordinal), "Short values must not be redacted.");

            Dictionary<string, string> patterns = new Dictionary<string, string>
            {
                { "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\n-----END OPENSSH PRIVATE KEY-----", "private_key" },
                { "key id AKIAIOSFODNN7EXAMPLE in the log", "aws_access_key" },
                { "aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "aws_secret_key" },
                { "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature", "bearer" },
                { "postgres://app:Sup3rSecret@db.internal:5432/app", "connection_string" },
                { "Server=db;User Id=sa;Password=Sup3rSecret;", "password_pair" },
                { "{\"password\": \"Sup3rSecret\"}", "password_pair" },
                { "client_secret=abcdefghijklmnop", "generic" }
            };
            foreach (KeyValuePair<string, string> pattern in patterns)
            {
                string result = SecretScrubber.Shared.Scrub(pattern.Key)!;
                Assert.True(result.Contains("[redacted:" + pattern.Value + "]", StringComparison.Ordinal), "Pattern " + pattern.Value + " missed: " + result);
                Assert.True(!result.Contains("Sup3rSecret", StringComparison.Ordinal) && !result.Contains("wJalrXUtnFEMI", StringComparison.Ordinal) && !result.Contains("AKIAIOSFODNN7EXAMPLE", StringComparison.Ordinal), "Secret survived: " + result);
            }
            Assert.Equal("password=[redacted:password_pair]&user=bob", SecretScrubber.Shared.Scrub("password=hunter22&user=bob"), "The key name is kept");
            Assert.Equal("nothing secret here", SecretScrubber.Shared.Scrub("nothing secret here"), "Plain text unchanged");
            Assert.True(SecretScrubber.Shared.Scrub(null) == null, "Null stays null.");
            return Task.CompletedTask;
        }

        private static Task ImportSshAsync(CancellationToken token)
        {
            ExpectKey("ed25519-openssh.key", "OpenSSH", "ssh-ed25519", "SHA256:j5Spkst5F2JQWeVxPi0JLPcfrL3BdLlERYTHJWuKxUo", false, "hugo-test-key", null);
            ExpectKey("ed25519-encrypted.key", "OpenSSH", "ssh-ed25519", "SHA256:3WGjeQd/WZKc3gvViV1na5J7ubjzQlwYIgcNa4t0N7Y", true, null, null);
            ExpectKey("ecdsa-openssh.key", "OpenSSH", "ecdsa-sha2-nistp256", "SHA256:nyCXfTkYP2+2QSGONu4X/bL2SMGcmp4JeGXCF1qMa/w", false, "ec-key", null);
            ExpectKey("rsa-pkcs1.pem", "PKCS#1", "ssh-rsa", "SHA256:/ZiKtXfh8CvkeqWtvikc7qFWO7stzT5JDAdNjpz69Dg", false, null, 2048);
            ExpectKey("rsa-pkcs8.pem", "PKCS#8", "ssh-rsa", "SHA256:/ZiKtXfh8CvkeqWtvikc7qFWO7stzT5JDAdNjpz69Dg", false, null, 2048);
            ExpectKey("ecdsa-sec1.pem", "SEC1", "ecdsa-sha2-nistp256", "SHA256:nyCXfTkYP2+2QSGONu4X/bL2SMGcmp4JeGXCF1qMa/w", false, null, 256);

            using (RSA rsa = RSA.Create(2048))
            {
                string encrypted = rsa.ExportEncryptedPkcs8PrivateKeyPem("pass-phrase-1", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10000));
                SecretImportResult locked = SecretImporter.Parse(new SecretImportRequest { Content = encrypted });
                Assert.True(locked.Encrypted && locked.Fingerprint == null && locked.Warnings.Count > 0, "Encrypted PKCS#8 without a passphrase.");
                SecretImportResult unlocked = SecretImporter.Parse(new SecretImportRequest { Content = encrypted, Passphrase = "pass-phrase-1" });
                Assert.True(unlocked.Encrypted && unlocked.Fingerprint != null && unlocked.Fingerprint.StartsWith("SHA256:", StringComparison.Ordinal), "Encrypted PKCS#8 with the passphrase.");
                Assert.Equal(SecretKindEnum.PrivateKey, unlocked.Kind, "Kind");
            }

            bool rejected = false;
            try
            {
                SecretImporter.Parse(new SecretImportRequest { Content = "just some text" });
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert.True(rejected, "Unrecognized content accepted.");
            return Task.CompletedTask;
        }

        private static Task ImportOtherAsync(CancellationToken token)
        {
            string json = "{\"type\":\"service_account\",\"project_id\":\"hugo-prod\",\"private_key_id\":\"abc123\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\\nAAAA\\n-----END PRIVATE KEY-----\\n\",\"client_email\":\"agent@hugo-prod.iam.gserviceaccount.com\",\"client_id\":\"1\"}";
            SecretImportResult gcp = SecretImporter.Parse(new SecretImportRequest { Content = json });
            Assert.Equal(SecretKindEnum.ServiceAccountJson, gcp.Kind, "GCP kind");
            Assert.Equal("hugo-prod", gcp.ProjectId, "Project");
            Assert.Equal("agent@hugo-prod.iam.gserviceaccount.com", gcp.ClientEmail, "Client email");
            Assert.True(gcp.HasPrivateKey, "Private key present.");

            string kubeconfig = "apiVersion: v1\nkind: Config\ncurrent-context: prod\nclusters:\n- name: prod-cluster\n  cluster:\n    server: https://k8s.example.com:6443\n    certificate-authority-data: AAAA\ncontexts:\n- name: prod\n  context:\n    cluster: prod-cluster\n    user: admin\n    namespace: shop\n- name: dev\n  context:\n    cluster: dev-cluster\n    user: dev\nusers:\n- name: admin\n  user:\n    token: abc\n";
            SecretImportResult kube = SecretImporter.Parse(new SecretImportRequest { Content = kubeconfig });
            Assert.Equal(SecretKindEnum.Kubeconfig, kube.Kind, "Kubeconfig kind");
            Assert.Equal("prod", kube.CurrentContext, "Current context");
            Assert.Equal(2, kube.Contexts.Count, "Contexts");
            KubeconfigContextSummary prod = kube.Contexts.First(c => c.Name == "prod");
            Assert.Equal("https://k8s.example.com:6443", prod.Server, "Server");
            Assert.Equal("shop", prod.Namespace, "Namespace");

            using (ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                CertificateRequest request = new CertificateRequest("CN=hugo.test", ecdsa, HashAlgorithmName.SHA256);
                using (X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)))
                {
                    string pem = certificate.ExportCertificatePem() + "\n" + ecdsa.ExportPkcs8PrivateKeyPem();
                    SecretImportResult cert = SecretImporter.Parse(new SecretImportRequest { Content = pem });
                    Assert.Equal(SecretKindEnum.Certificate, cert.Kind, "Certificate kind");
                    Assert.Equal("CN=hugo.test", cert.Subject, "Subject");
                    Assert.True(cert.HasPrivateKey, "Private key detected.");
                    Assert.Equal("SHA256:" + Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)), cert.Fingerprint, "Thumbprint");
                    Assert.True(cert.NotAfterUtc.HasValue && cert.NotAfterUtc.Value > DateTime.UtcNow, "Validity.");
                }
            }
            return Task.CompletedTask;
        }

        private static Task FieldRulesAsync(CancellationToken token)
        {
            Assert.True(SecretFieldRules.Accepts(SecretKindEnum.AccessKeyPair, "secretAccessKey") && SecretFieldRules.Accepts(SecretKindEnum.AccessKeyPair, "sessionToken"), "Access key pair fields.");
            Assert.True(!SecretFieldRules.Accepts(SecretKindEnum.Password, "token"), "Password accepts token.");
            Assert.True(SecretFieldRules.Accepts(SecretKindEnum.Generic, "anything_goes.v2"), "Generic accepts named fields.");
            Assert.True(!SecretFieldRules.Accepts(SecretKindEnum.Generic, "bad name"), "Generic accepts spaces.");
            Assert.Equal("accessKeyId", SecretFieldRules.PrimaryField(SecretKindEnum.AccessKeyPair), "Primary field");
            return Task.CompletedTask;
        }

        #endregion

        #region Provider-Cases

        private static async Task ServiceAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                SecretCipher cipher = new SecretCipher(RandomNumberGenerator.GetBytes(32), 1);
                SecretService service = new SecretService(db, cipher);

                ValidationException? missing = await ThrowsAsync<ValidationException>(() => service.CreateAsync("ten_a", null, new SecretRequest { Name = "aws", Kind = SecretKindEnum.AccessKeyPair, Values = new Dictionary<string, string> { { "accessKeyId", "AKIA1" } } }, token)).ConfigureAwait(false);
                Assert.True(missing != null && missing.Codes.ContainsValue("RequiredFieldMissing"), "Missing required field.");
                ValidationException? unknown = await ThrowsAsync<ValidationException>(() => service.CreateAsync("ten_a", null, new SecretRequest { Name = "pw", Kind = SecretKindEnum.Password, Values = new Dictionary<string, string> { { "password", "x1" }, { "token", "y" } } }, token)).ConfigureAwait(false);
                Assert.True(unknown != null && unknown.Codes.ContainsValue("UnknownField"), "Unknown field.");
                ValidationException? mask = await ThrowsAsync<ValidationException>(() => service.CreateAsync("ten_a", null, new SecretRequest { Name = "pw", Kind = SecretKindEnum.Password, Values = new Dictionary<string, string> { { "password", SecretFieldRules.MaskPlaceholder } } }, token)).ConfigureAwait(false);
                Assert.True(mask != null, "The mask placeholder was accepted as a value.");

                Secret created = await service.CreateAsync("ten_a", "usr_1", new SecretRequest { Name = "aws", Kind = SecretKindEnum.AccessKeyPair, Values = new Dictionary<string, string> { { "accessKeyId", "AKIAIOSFODNN7EXAMPLE" }, { "secretAccessKey", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY" } } }, token).ConfigureAwait(false);
                Assert.Equal("****MPLE", created.Fingerprint, "Last four of the access key id");
                Assert.Equal("usr_1", created.OwnerUserId, "Owner");
                await ThrowsAsync<ConflictException>(() => service.CreateAsync("ten_a", null, new SecretRequest { Name = "aws", Kind = SecretKindEnum.ApiToken, Values = new Dictionary<string, string> { { "token", "abcdef" } } }, token)).ConfigureAwait(false);
                Secret otherTenant = await service.CreateAsync("ten_b", null, new SecretRequest { Name = "aws", Kind = SecretKindEnum.ApiToken, Values = new Dictionary<string, string> { { "token", "abcdef" } } }, token).ConfigureAwait(false);

                Secret stored = Assert.NotNull(await db.Secrets.ReadAsync(created.Id, token).ConfigureAwait(false), "Stored");
                Assert.True(stored.Values.All(v => !v.Ciphertext.Contains("AKIA", StringComparison.Ordinal) && !v.Ciphertext.Contains("wJalr", StringComparison.Ordinal)), "Plaintext stored.");

                SecretValueUpdate update = await service.UpdateValuesAsync("ten_a", created.Id, new SecretValueRequest { Values = new Dictionary<string, string> { { "accessKeyId", SecretFieldRules.MaskPlaceholder }, { "secretAccessKey", "new-secret-access-key-value" }, { "sessionToken", "session-token-value" } } }, token).ConfigureAwait(false);
                Assert.Equal("secretAccessKey,sessionToken", String.Join(",", update.ChangedFields), "Changed fields");
                Dictionary<string, string> resolved = await service.ResolveAsync("ten_a", created.Id, token).ConfigureAwait(false);
                Assert.Equal("AKIAIOSFODNN7EXAMPLE", resolved["accessKeyId"], "The mask kept the stored value");
                Assert.Equal("new-secret-access-key-value", resolved["secretAccessKey"], "Replaced value");
                Assert.True(!resolved.Values.Contains(SecretFieldRules.MaskPlaceholder), "The mask placeholder was persisted.");

                SecretValueUpdate removed = await service.UpdateValuesAsync("ten_a", created.Id, new SecretValueRequest { Remove = new List<string> { "sessionToken" } }, token).ConfigureAwait(false);
                Assert.Equal("accessKeyId,secretAccessKey", String.Join(",", removed.Secret.Fields), "Field removed");
                await ThrowsAsync<ValidationException>(() => service.UpdateValuesAsync("ten_a", created.Id, new SecretValueRequest { Remove = new List<string> { "accessKeyId" } }, token)).ConfigureAwait(false);
                await ThrowsAsync<ValidationException>(() => service.UpdateMetadataAsync("ten_a", created.Id, new SecretRequest { Name = "aws", Kind = SecretKindEnum.ApiToken }, token)).ConfigureAwait(false);
                await ThrowsAsync<NotFoundException>(() => service.ReadAsync("ten_b", created.Id, token)).ConfigureAwait(false);
                await ThrowsAsync<NotFoundException>(() => service.RevealAsync("ten_b", created.Id, token)).ConfigureAwait(false);
                await ThrowsAsync<NotFoundException>(() => service.DeleteAsync("ten_b", created.Id, token)).ConfigureAwait(false);

                string keyText = File.ReadAllText(Fixture("ed25519-openssh.key"));
                Secret sshKey = await service.CreateAsync("ten_a", null, new SecretRequest { Name = "ssh", Kind = SecretKindEnum.PrivateKey, Values = new Dictionary<string, string> { { "privateKey", keyText } } }, token).ConfigureAwait(false);
                Assert.Equal("SHA256:j5Spkst5F2JQWeVxPi0JLPcfrL3BdLlERYTHJWuKxUo", sshKey.Fingerprint, "SSH fingerprint");

                FakeReferrers referrers = new FakeReferrers(created.Id);
                service.AddReferrerProvider(referrers);
                List<SecretReferrer> found = await service.ReferrersAsync("ten_a", created.Id, token).ConfigureAwait(false);
                Assert.Equal(1, found.Count, "Referrers");
                ConflictException? inUse = await ThrowsAsync<ConflictException>(() => service.DeleteAsync("ten_a", created.Id, token)).ConfigureAwait(false);
                Assert.True(inUse != null && inUse.Code == "SecretInUse" && inUse.Message.Contains("res-prod", StringComparison.Ordinal), "409 lists the referrers.");
                await service.DeleteAsync("ten_a", sshKey.Id, token).ConfigureAwait(false);
                Assert.True(await db.Secrets.ReadAsync(sshKey.Id, token).ConfigureAwait(false) == null, "Unreferenced secret deleted.");
                Assert.True(await db.Secrets.ReadAsync(otherTenant.Id, token).ConfigureAwait(false) != null, "Other tenant secret untouched.");

                SecretReveal reveal = await service.RevealAsync("ten_a", created.Id, token).ConfigureAwait(false);
                Assert.Equal("new-secret-access-key-value", reveal.Values["secretAccessKey"], "Reveal values");
                Assert.True(Assert.NotNull(await db.Secrets.ReadAsync(created.Id, token).ConfigureAwait(false), "After reveal").LastRevealedUtc.HasValue, "Reveal time recorded.");
            }
        }

        private static async Task CanaryAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                byte[] keyA = RandomNumberGenerator.GetBytes(32);
                byte[] keyB = RandomNumberGenerator.GetBytes(32);
                SecretCipher a = new SecretCipher(keyA, 1);
                await MasterKeyGuard.VerifyAsync(db, a, null, token).ConfigureAwait(false);
                MasterKeyVersion canary = Assert.NotNull(await db.MasterKeys.ReadAsync(1, token).ConfigureAwait(false), "Canary");
                Assert.True(canary.IsCurrent, "Canary current.");
                await MasterKeyGuard.VerifyAsync(db, a, null, token).ConfigureAwait(false);

                // A different key with the same version and nothing encrypted: the canary itself refuses it.
                Assert.True(await ThrowsAsync<MasterKeyException>(() => MasterKeyGuard.VerifyAsync(db, new SecretCipher(keyB, 1), null, token)).ConfigureAwait(false) != null, "Wrong key with the same version accepted.");

                SecretService service = new SecretService(db, a);
                await service.CreateAsync("ten_a", null, new SecretRequest { Name = "x", Kind = SecretKindEnum.ApiToken, Values = new Dictionary<string, string> { { "token", "token-value-1" } } }, token).ConfigureAwait(false);

                // A new key version without the previous key while encrypted rows exist: refused.
                MasterKeyException? refused = await ThrowsAsync<MasterKeyException>(() => MasterKeyGuard.VerifyAsync(db, new SecretCipher(keyB, 2), null, token)).ConfigureAwait(false);
                Assert.True(refused != null && refused.Message.Contains("Refusing to start", StringComparison.Ordinal), "New key without the previous key accepted.");

                // A new key version with the previous key: accepted, and becomes current.
                await MasterKeyGuard.VerifyAsync(db, new SecretCipher(keyB, 2, keyA, 1), null, token).ConfigureAwait(false);
                List<MasterKeyVersion> versions = await db.MasterKeys.ReadAllAsync(token).ConfigureAwait(false);
                Assert.Equal(2, versions.Count, "Two key versions");
                Assert.True(versions.Single(v => v.KeyVersion == 2).IsCurrent && !versions.Single(v => v.KeyVersion == 1).IsCurrent, "Version 2 current.");
            }
        }

        private static async Task RotationServiceAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                byte[] keyA = RandomNumberGenerator.GetBytes(32);
                byte[] keyB = RandomNumberGenerator.GetBytes(32);
                SecretCipher v1 = new SecretCipher(keyA, 1);
                SecretService service = new SecretService(db, v1);
                List<string> ids = new List<string>();
                for (int i = 0; i < 7; i++)
                {
                    Secret secret = await service.CreateAsync(i % 2 == 0 ? "ten_a" : "ten_b", null, new SecretRequest { Name = "s" + i, Kind = SecretKindEnum.AccessKeyPair, Values = new Dictionary<string, string> { { "accessKeyId", "AKIA" + i + "XXXXXXXX" }, { "secretAccessKey", "secret-" + i } } }, token).ConfigureAwait(false);
                    ids.Add(secret.Id);
                }
                Credential credential = new Credential();
                credential.TenantId = "ten_a";
                credential.UserId = "usr_1";
                credential.Name = "c";
                credential.AccessKey = SecretGenerator.NewAccessKey();
                credential.SecretKeyEncrypted = v1.EncryptCredential("secret_value_xyz", credential.Id);
                await db.Credentials.CreateAsync(credential, token).ConfigureAwait(false);

                SecretCipher v2 = new SecretCipher(keyB, 2, keyA, 1);
                MasterKeyRotationService rotation = new MasterKeyRotationService(db, v2);
                rotation.BatchSize = 3;
                int progress = 0;
                rotation.Progress += (sender, status) => progress++;
                MasterKeyRotationStatus status = await rotation.RunAsync(token).ConfigureAwait(false);
                Assert.Equal(14L, status.Total - 1, "Values found (plus one credential)");
                Assert.Equal(15L, status.Completed, "Rows re-encrypted");
                Assert.Equal(0L, status.Failed, "Failures");
                Assert.Equal(0L, status.Remaining, "Remaining");
                Assert.True(progress >= 5, "Progress reported per batch: " + progress);

                SecretCipher v2Only = new SecretCipher(keyB, 2);
                SecretService fresh = new SecretService(db, v2Only);
                for (int i = 0; i < ids.Count; i++)
                {
                    Dictionary<string, string> values = await fresh.ResolveAsync(i % 2 == 0 ? "ten_a" : "ten_b", ids[i], token).ConfigureAwait(false);
                    Assert.Equal("secret-" + i, values["secretAccessKey"], "Value " + i + " under the new key");
                    Assert.Equal(2, Assert.NotNull(await db.Secrets.ReadAsync(ids[i], token).ConfigureAwait(false), "Secret").KeyVersion, "Secret key version");
                }
                Credential rotated = Assert.NotNull(await db.Credentials.ReadAsync(credential.Id, token).ConfigureAwait(false), "Credential");
                Assert.Equal("secret_value_xyz", v2Only.DecryptCredential(rotated.SecretKeyEncrypted, credential.Id), "Credential secret under the new key");

                MasterKeyRotationStatus again = await rotation.RunAsync(token).ConfigureAwait(false);
                Assert.Equal(0L, again.Completed, "A second run finds nothing to do (resumable, idempotent)");
            }
        }

        private static async Task RoutesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string basePath = "/v1.0/api/tenants/" + host.TenantId + "/secrets";
                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    TestResponse createdRaw = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, basePath, new { name = "prod-db", kind = "ConnectionString", description = "primary", values = new { connectionString = "Host=db;Password=Plaintext-Value-42" } }, token).ConfigureAwait(false);
                    Assert.Equal(201, createdRaw.StatusCode, "Create (" + createdRaw.Body + ")");
                    Assert.True(!createdRaw.Body.Contains("Plaintext-Value-42", StringComparison.Ordinal), "Create returned the value.");
                    Secret created = Assert.NotNull(Hugo.Core.Serialization.Serializer.Deserialize<Secret>(createdRaw.Body), "Created");
                    Assert.Equal("connectionString", String.Join(",", created.Fields), "Fields");

                    TestResponse invalid = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, basePath, new { name = "bad", kind = "Password", values = new { token = "x" } }, token).ConfigureAwait(false);
                    Assert.Equal(400, invalid.StatusCode, "Unknown field (" + invalid.Body + ")");
                    Assert.Equal(409, (await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, basePath, new { name = "prod-db", kind = "Password", values = new { password = "abcdef" } }, token).ConfigureAwait(false)).StatusCode, "Duplicate name");

                    TestResponse list = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Get, basePath, null, token).ConfigureAwait(false);
                    Assert.True(list.StatusCode == 200 && list.Body.Contains("connectionString", StringComparison.Ordinal) && !list.Body.Contains("Plaintext-Value-42", StringComparison.Ordinal) && !list.Body.Contains("ciphertext", StringComparison.OrdinalIgnoreCase), "List leaked or lacks field names: " + list.Body);
                    TestResponse read = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Get, basePath + "/" + created.Id, null, token).ConfigureAwait(false);
                    Assert.True(read.StatusCode == 200 && read.Body.Contains("\"referrers\":[]", StringComparison.Ordinal) && !read.Body.Contains("Plaintext", StringComparison.Ordinal), "Read: " + read.Body);
                    List<SecretReferrer> referrers = await HttpTestHelpers.SendAsync<List<SecretReferrer>>(anderson, HttpMethod.Get, basePath + "/" + created.Id + "/referrers", null, 200, token).ConfigureAwait(false);
                    Assert.Equal(0, referrers.Count, "Referrers");

                    Secret renamed = await HttpTestHelpers.SendAsync<Secret>(anderson, HttpMethod.Put, basePath + "/" + created.Id, new { name = "prod-db-primary", kind = "ConnectionString", description = "renamed" }, 200, token).ConfigureAwait(false);
                    Assert.Equal("prod-db-primary", renamed.Name, "Rename");
                    TestResponse values = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Put, basePath + "/" + created.Id + "/values", new { values = new { connectionString = "Host=db;Password=Rotated-Value-43" } }, token).ConfigureAwait(false);
                    Assert.True(values.StatusCode == 200 && !values.Body.Contains("Rotated-Value-43", StringComparison.Ordinal), "Values update: " + values.Body);

                    SecretImportResult parsed = await HttpTestHelpers.SendAsync<SecretImportResult>(anderson, HttpMethod.Post, basePath + "/parse", new { content = File.ReadAllText(Fixture("ecdsa-openssh.key")) }, 200, token).ConfigureAwait(false);
                    Assert.Equal("SHA256:nyCXfTkYP2+2QSGONu4X/bL2SMGcmp4JeGXCF1qMa/w", parsed.Fingerprint, "Parse route fingerprint");
                    Assert.Equal(400, (await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, basePath + "/parse", new { content = "nonsense" }, token).ConfigureAwait(false)).StatusCode, "Parse route rejects nonsense");
                    EnumerationResult<Secret> afterParse = await HttpTestHelpers.SendAsync<EnumerationResult<Secret>>(anderson, HttpMethod.Get, basePath, null, 200, token).ConfigureAwait(false);
                    Assert.Equal(1L, afterParse.TotalRecords, "Parsing stores nothing");

                    Assert.Equal(204, (await HttpTestHelpers.SendAsync(anderson, HttpMethod.Delete, basePath + "/" + created.Id, null, token).ConfigureAwait(false)).StatusCode, "Delete");
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(anderson, HttpMethod.Get, basePath + "/" + created.Id, null, token).ConfigureAwait(false)).StatusCode, "Read after delete");
                }

                AuditPage audit = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit?pageSize=200", null, 200, token).ConfigureAwait(false);
                foreach (AuditEventTypeEnum type in new[] { AuditEventTypeEnum.SecretCreated, AuditEventTypeEnum.SecretUpdated, AuditEventTypeEnum.SecretDeleted })
                {
                    Assert.True(audit.Objects.Any(r => r.EventType == type), type + " not audited.");
                }
                Assert.True(audit.Objects.All(r => r.Description == null || (!r.Description.Contains("Plaintext-Value", StringComparison.Ordinal) && !r.Description.Contains("Rotated-Value", StringComparison.Ordinal))), "An audit description leaked a value.");

                RequestHistoryPage history = await WaitForHistoryAsync(host, "/secrets", 4, token).ConfigureAwait(false);
                foreach (RequestHistoryEntry entry in history.Objects)
                {
                    RequestHistoryEntry detail = await HttpTestHelpers.SendAsync<RequestHistoryEntry>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history/" + entry.Id, null, 200, token).ConfigureAwait(false);
                    string all = (detail.RequestBody ?? String.Empty) + (detail.ResponseBody ?? String.Empty) + String.Join(",", detail.Headers.Select(h => h.Value));
                    Assert.True(!all.Contains("Plaintext-Value-42", StringComparison.Ordinal) && !all.Contains("Rotated-Value-43", StringComparison.Ordinal), "Request history captured a secret value: " + entry.Path);
                }
            }
        }

        private static async Task RevealAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string basePath = "/v1.0/api/tenants/" + host.TenantId + "/secrets";
                Secret secret = await HttpTestHelpers.SendAsync<Secret>(host.AdminClient, HttpMethod.Post, basePath, new { name = "reveal-me", kind = "Password", values = new { username = "svc", password = "Reveal-Plaintext-77" } }, 201, token).ConfigureAwait(false);
                string revealPath = basePath + "/" + secret.Id + "/reveal";

                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    TestResponse noReauth = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, revealPath, new { reason = "incident 42" }, token).ConfigureAwait(false);
                    Assert.True(noReauth.StatusCode == 403 && noReauth.Body.Contains("ReauthenticationRequired", StringComparison.Ordinal), "Reveal without re-authentication: " + noReauth.Body);
                    Assert.Equal(401, (await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, "/v1.0/token/reauthenticate", new { password = "wrong-password" }, token).ConfigureAwait(false)).StatusCode, "Re-authentication with the wrong password");
                    ReauthenticateResponse reauth = await HttpTestHelpers.SendAsync<ReauthenticateResponse>(anderson, HttpMethod.Post, "/v1.0/token/reauthenticate", new { password = TestServerHost.ActivatedPassword }, 200, token).ConfigureAwait(false);
                    Assert.True(reauth.RevealAllowedUntilUtc > DateTime.UtcNow.AddMinutes(4), "Reveal window.");

                    TestResponse noReason = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, revealPath, new { reason = "  " }, token).ConfigureAwait(false);
                    Assert.Equal(400, noReason.StatusCode, "Reveal without a reason");
                    SecretRevealResponse revealed = await HttpTestHelpers.SendAsync<SecretRevealResponse>(anderson, HttpMethod.Post, revealPath, new { reason = "incident 42" }, 200, token).ConfigureAwait(false);
                    Assert.Equal("Reveal-Plaintext-77", revealed.Values["password"], "Revealed password");
                    Assert.Equal("svc", revealed.Values["username"], "Revealed username");
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, basePath + "/sec_missing/reveal", new { reason = "x" }, token).ConfigureAwait(false)).StatusCode, "Reveal of a missing secret");
                }

                AuditPage audit = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit?eventType=SecretRevealed", null, 200, token).ConfigureAwait(false);
                Assert.Equal(1L, audit.TotalCount, "One reveal audited");
                AuditRecord record = audit.Objects[0];
                Assert.True(record.Reason == "incident 42" && record.ResourceId == secret.Id && record.PrincipalName == "anderson@hugo.local", "Reveal audit fields.");
                Assert.True(record.Description != null && !record.Description.Contains("Reveal-Plaintext-77", StringComparison.Ordinal), "Audit description leaked the value.");
                AuditPage reauthAudit = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit?eventType=SessionReauthenticated", null, 200, token).ConfigureAwait(false);
                Assert.Equal(1L, reauthAudit.TotalCount, "Re-authentication audited");

                RequestHistoryPage history = await WaitForHistoryAsync(host, "/reveal", 4, token).ConfigureAwait(false);
                RequestHistoryEntry success = history.Objects.First(o => o.StatusCode == 200 && o.Path.EndsWith("/reveal", StringComparison.Ordinal));
                RequestHistoryEntry detail = await HttpTestHelpers.SendAsync<RequestHistoryEntry>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history/" + success.Id, null, 200, token).ConfigureAwait(false);
                Assert.True(detail.ResponseBody == null && detail.RequestBody == null, "Reveal bodies captured: " + detail.ResponseBody);
                Assert.True(detail.ResponseBodyBytes > 0, "Reveal entry still records its size.");
                Assert.True(detail.Headers.All(h => !h.Value.Contains("Reveal-Plaintext-77", StringComparison.Ordinal)), "Reveal headers leaked the value.");

                // Builder may manage secrets but never reveal them, even after re-authentication.
                await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/users", new UserRequest { Email = "builder@reveal.local", Password = "password123" }, 201, token).ConfigureAwait(false);
                User builder = Assert.NotNull(await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "builder@reveal.local", token).ConfigureAwait(false), "Builder");
                await HttpTestHelpers.SendAsync<UserRoleAssignment>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/user-role-assignments", new AssignmentRequest { UserId = builder.Id, RoleName = "Builder" }, 201, token).ConfigureAwait(false);
                using (HttpClient client = await host.UserClientAsync("builder@reveal.local", "password123", host.TenantId, token).ConfigureAwait(false))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, basePath + "/" + secret.Id, null, token).ConfigureAwait(false)).StatusCode, "Builder reads metadata");
                    await HttpTestHelpers.SendAsync<ReauthenticateResponse>(client, HttpMethod.Post, "/v1.0/token/reauthenticate", new { password = "password123" }, 200, token).ConfigureAwait(false);
                    TestResponse denied = await HttpTestHelpers.SendAsync(client, HttpMethod.Post, revealPath, new { reason = "curious" }, token).ConfigureAwait(false);
                    Assert.True(denied.StatusCode == 403 && denied.Body.Contains("AuthorizationDenied", StringComparison.Ordinal) && denied.Body.Contains("Reveal", StringComparison.Ordinal), "Builder reveal: " + denied.Body);
                }

                // Credentials are machine identities and never reveal.
                CredentialCreateResponse credential = await HttpTestHelpers.SendAsync<CredentialCreateResponse>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/credentials", new CredentialRequest { Name = "ci", UserId = (await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "anderson@hugo.local", token).ConfigureAwait(false))!.Id, RoleName = "TenantAdmin" }, 201, token).ConfigureAwait(false);
                using (HttpClient machine = host.CredentialClient(credential.AccessKey, credential.SecretKey))
                {
                    TestResponse denied = await HttpTestHelpers.SendAsync(machine, HttpMethod.Post, revealPath, new { reason = "automation" }, token).ConfigureAwait(false);
                    Assert.True(denied.StatusCode == 403 && denied.Body.Contains("ReauthenticationRequired", StringComparison.Ordinal), "Credential reveal: " + denied.Body);
                }
                Assert.Equal(1L, (await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit?eventType=SecretRevealed", null, 200, token).ConfigureAwait(false)).TotalCount, "Denied reveals are not SecretRevealed events");
            }
        }

        private static async Task RevealRateLimitAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => s.Secrets.RevealRateLimitPerMinute = 2, token).ConfigureAwait(false))
            {
                Secret secret = await HttpTestHelpers.SendAsync<Secret>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/secrets", new { name = "limited", kind = "ApiToken", values = new { token = "limited-token-value" } }, 201, token).ConfigureAwait(false);
                List<int> statuses = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    statuses.Add((await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/secrets/" + secret.Id + "/reveal", new { reason = "rate limit test" }, token).ConfigureAwait(false)).StatusCode);
                }
                Assert.True(statuses[0] == 200 && statuses[1] == 200 && statuses[2] == 429 && statuses[3] == 429, "Statuses: " + String.Join(",", statuses));
            }
        }

        private static async Task RotationRouteAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            MasterKeyOptions v1 = TestServerHost.NewKeyOptions();
            Hugo.Core.Database.DatabaseSettings database;
            string secretId;
            string accessKey;
            string secretKey;
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token, v1).ConfigureAwait(false))
            {
                database = host.Settings.Database;
                Secret secret = await HttpTestHelpers.SendAsync<Secret>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/secrets", new { name = "rotate-me", kind = "Password", values = new { password = "Rotation-Plaintext-9" } }, 201, token).ConfigureAwait(false);
                secretId = secret.Id;
                User anderson = Assert.NotNull(await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "anderson@hugo.local", token).ConfigureAwait(false), "Anderson");
                CredentialCreateResponse credential = await HttpTestHelpers.SendAsync<CredentialCreateResponse>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/credentials", new CredentialRequest { Name = "rotating", UserId = anderson.Id, RoleName = "Viewer" }, 201, token).ConfigureAwait(false);
                accessKey = credential.AccessKey;
                secretKey = credential.SecretKey;
            }

            MasterKeyOptions v2 = new MasterKeyOptions();
            v2.EnvironmentKey = TestServerHost.NewKeyOptions().EnvironmentKey;
            v2.EnvironmentPreviousKey = v1.EnvironmentKey;
            v2.KeyVersion = 2;
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => s.Database = database, token, v2).ConfigureAwait(false))
            {
                MasterKeyRotationStatus before = await HttpTestHelpers.SendAsync<MasterKeyRotationStatus>(host.AdminClient, HttpMethod.Get, "/v1.0/api/system/master-key", null, 200, token).ConfigureAwait(false);
                Assert.True(before.Remaining == 1 && before.TargetKeyVersion == 2, "Pending before rotation: " + before.Remaining);
                using (HttpClient machine = host.CredentialClient(accessKey, secretKey))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(machine, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "Credential works through the previous key");
                }
                SecretRevealResponse early = await HttpTestHelpers.SendAsync<SecretRevealResponse>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/secrets/" + secretId + "/reveal", new { reason = "verify before rotation" }, 200, token).ConfigureAwait(false);
                Assert.Equal("Rotation-Plaintext-9", early.Values["password"], "Previous key reads the value");

                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(anderson, HttpMethod.Post, "/v1.0/api/system/master-key/rotate", null, token).ConfigureAwait(false)).StatusCode, "Tenant administrators cannot rotate");
                }
                TestResponse started = await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Post, "/v1.0/api/system/master-key/rotate", null, token).ConfigureAwait(false);
                Assert.True(started.StatusCode == 202 || started.StatusCode == 409, "Rotation start: " + started.StatusCode);
                MasterKeyRotationStatus status = before;
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    status = await HttpTestHelpers.SendAsync<MasterKeyRotationStatus>(host.AdminClient, HttpMethod.Get, "/v1.0/api/system/master-key", null, 200, token).ConfigureAwait(false);
                    if (!status.Running && status.CompletedUtc.HasValue) break;
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                Assert.True(!status.Running && status.Remaining == 0 && status.Failed == 0 && status.Completed == 2, "Rotation finished: completed " + status.Completed + ", remaining " + status.Remaining + ", failed " + status.Failed + ", error " + status.LastError);
                Hugo.Core.Requests.AuditFilter filter = new Hugo.Core.Requests.AuditFilter();
                filter.EventType = AuditEventTypeEnum.MasterKeyRotated;
                Assert.True((await host.Server.Context.Database.Audit.EnumerateAsync(filter, token).ConfigureAwait(false)).TotalCount >= 1, "Rotation audited.");
            }

            MasterKeyOptions v2Only = new MasterKeyOptions();
            v2Only.EnvironmentKey = v2.EnvironmentKey;
            v2Only.KeyVersion = 2;
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => s.Database = database, token, v2Only).ConfigureAwait(false))
            {
                SecretRevealResponse after = await HttpTestHelpers.SendAsync<SecretRevealResponse>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/secrets/" + secretId + "/reveal", new { reason = "verify after rotation" }, 200, token).ConfigureAwait(false);
                Assert.Equal("Rotation-Plaintext-9", after.Values["password"], "New key alone reads the value");
                using (HttpClient machine = host.CredentialClient(accessKey, secretKey))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(machine, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "Credential works under the new key alone");
                }
            }

            MasterKeyOptions wrong = TestServerHost.NewKeyOptions();
            wrong.KeyVersion = 2;
            bool refused = false;
            try
            {
                await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => s.Database = database, token, wrong).ConfigureAwait(false))
                {
                }
            }
            catch (MasterKeyException)
            {
                refused = true;
            }
            Assert.True(refused, "The server started with a key that does not decrypt the canary.");
        }

        #endregion

        #region Private-Methods

        private static string Fixture(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", "secrets", name);
        }

        private static void ExpectKey(string file, string format, string keyType, string fingerprint, bool encrypted, string? comment, int? bits)
        {
            SecretImportResult result;
            try
            {
                result = SecretImporter.Parse(new SecretImportRequest { Content = File.ReadAllText(Fixture(file)) });
            }
            catch (ArgumentException e)
            {
                throw new TestAssertionException(file + ": " + e.Message);
            }
            Assert.Equal(SecretKindEnum.PrivateKey, result.Kind, file + " kind");
            Assert.Equal(format, result.Format, file + " format");
            Assert.Equal(keyType, result.KeyType, file + " key type");
            Assert.Equal(fingerprint, result.Fingerprint, file + " fingerprint");
            Assert.Equal(encrypted, result.Encrypted, file + " encrypted");
            if (comment != null) Assert.Equal(comment, result.Comment, file + " comment");
            if (bits.HasValue) Assert.Equal(bits, result.KeySizeBits, file + " key size");
            Assert.Equal("privateKey", result.SuggestedField, file + " field");
        }

        private static void Fails(Action action, string what)
        {
            bool failed = false;
            try
            {
                action();
            }
            catch (Exception e) when (e is CryptographicException || e is FormatException)
            {
                failed = true;
            }
            Assert.True(failed, what + ": decryption should have failed.");
        }

        private static MasterKeyException? Throws(Action action)
        {
            try
            {
                action();
            }
            catch (MasterKeyException e)
            {
                return e;
            }
            return null;
        }

        private static async Task<T?> ThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T e)
            {
                return e;
            }
            throw new TestAssertionException("Expected " + typeof(T).Name + ".");
        }

        private static async Task<RequestHistoryPage> WaitForHistoryAsync(TestServerHost host, string pathContains, int minimum, CancellationToken token)
        {
            RequestHistoryPage page = new RequestHistoryPage();
            for (int attempt = 0; attempt < 50; attempt++)
            {
                page = await HttpTestHelpers.SendAsync<RequestHistoryPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history?pageSize=200&pathContains=" + Uri.EscapeDataString(pathContains), null, 200, token).ConfigureAwait(false);
                if (page.Objects.Count >= minimum) break;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            Assert.True(page.Objects.Count >= minimum, "Request history for " + pathContains + " has " + page.Objects.Count + " entries.");
            return page;
        }

        #endregion
    }
}
