namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// The secrets vault (P2.5): create, update values (the mask placeholder is never persisted), update metadata, read
    /// metadata, find referrers, delete (409 while referenced), reveal, and resolve for internal use. Every query is
    /// scoped by tenant; a secret in another tenant is not found. Thread-safe.
    /// </summary>
    public class SecretService
    {
        #region Public-Members

        /// <summary>
        /// Registered referrer providers. Later phases add providers for resources and model endpoints.
        /// </summary>
        public IReadOnlyCollection<ISecretReferrerProvider> ReferrerProviders
        {
            get
            {
                return _Providers.ToArray();
            }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly SecretCipher _Cipher;
        private readonly ConcurrentBag<ISecretReferrerProvider> _Providers = new ConcurrentBag<ISecretReferrerProvider>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="cipher">Secret cipher.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SecretService(DatabaseDriverBase database, SecretCipher cipher)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a referrer provider.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <exception cref="ArgumentNullException">Thrown when provider is null.</exception>
        public void AddReferrerProvider(ISecretReferrerProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            _Providers.Add(provider);
        }

        /// <summary>
        /// Create a secret with its field values.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="ownerUserId">Creating user, or null.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created secret (metadata only).</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="ConflictException">Thrown when the name is taken.</exception>
        public async Task<Secret> CreateAsync(string tenantId, string? ownerUserId, SecretRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Dictionary<string, string> errors = new Dictionary<string, string>();
            Dictionary<string, string> codes = new Dictionary<string, string>();
            ValidateMetadata(request, errors, codes, true);
            SecretKindEnum kind = request.Kind ?? SecretKindEnum.Generic;
            Dictionary<string, string> values = request.Values ?? new Dictionary<string, string>();
            ValidateValues(kind, values, errors, codes, true);
            List<string> missing = SecretFieldRules.Required(kind).Where(f => !values.ContainsKey(f)).ToList();
            if (missing.Count > 0)
            {
                errors["values"] = "A " + kind + " secret requires the field(s) " + String.Join(", ", missing) + ".";
                codes["values"] = "RequiredFieldMissing";
            }
            if (kind == SecretKindEnum.Generic && values.Count == 0)
            {
                errors["values"] = "A secret requires at least one field value.";
                codes["values"] = "RequiredFieldMissing";
            }
            if (errors.Count > 0) throw new ValidationException("The secret is invalid: " + String.Join(" ", errors.Values), errors, codes);

            string name = request.Name!.Trim();
            Secret? existing = await _Database.Secrets.ReadByNameAsync(tenantId, name, token).ConfigureAwait(false);
            if (existing != null) throw new ConflictException("NameConflict", "The name '" + name + "' is already used in this tenant.");

            Secret secret = new Secret();
            secret.TenantId = tenantId;
            secret.OwnerUserId = ownerUserId;
            secret.Name = name;
            secret.Description = request.Description;
            secret.Kind = kind;
            secret.ExpiresUtc = request.ExpiresUtc;
            foreach (KeyValuePair<string, string> field in values) secret.Values.Add(Encrypt(secret, field.Key, field.Value));
            Finish(secret, values);
            return await _Database.Secrets.CreateAsync(secret, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a secret (metadata and field names; values stay encrypted).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Secret.</returns>
        /// <exception cref="NotFoundException">Thrown when the secret does not exist in the tenant.</exception>
        public async Task<Secret> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(id)) throw new NotFoundException("Secret not found.");
            Secret? secret = await _Database.Secrets.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            return secret ?? throw new NotFoundException("Secret not found.");
        }

        /// <summary>
        /// Update name, description, and expiry. Omitted (null) name, kind, and description keep the stored values; an empty
        /// description clears it. The expiry is replaced, so a null expiry clears it. The kind cannot change: when supplied
        /// it must equal the stored kind.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Secret identifier.</param>
        /// <param name="request">Request; Values is ignored.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated secret.</returns>
        /// <exception cref="NotFoundException">Thrown when the secret does not exist in the tenant.</exception>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="ConflictException">Thrown when the secret is protected or the name is taken.</exception>
        public async Task<Secret> UpdateMetadataAsync(string tenantId, string id, SecretRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Secret secret = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (secret.IsProtected) throw new ConflictException("Protected", "Secret " + secret.Name + " is protected.");

            Dictionary<string, string> errors = new Dictionary<string, string>();
            Dictionary<string, string> codes = new Dictionary<string, string>();
            ValidateMetadata(request, errors, codes, false);
            if (request.Kind.HasValue && request.Kind.Value != secret.Kind)
            {
                errors["kind"] = "The kind of a secret cannot change; create a new secret instead.";
                codes["kind"] = "KindImmutable";
            }
            if (errors.Count > 0) throw new ValidationException("The secret is invalid: " + String.Join(" ", errors.Values), errors, codes);

            if (request.Name != null)
            {
                string name = request.Name.Trim();
                Secret? named = await _Database.Secrets.ReadByNameAsync(tenantId, name, token).ConfigureAwait(false);
                if (named != null && named.Id != secret.Id) throw new ConflictException("NameConflict", "The name '" + name + "' is already used in this tenant.");
                secret.Name = name;
            }
            if (request.Description != null) secret.Description = request.Description.Length == 0 ? null : request.Description;
            secret.ExpiresUtc = request.ExpiresUtc;
            return await _Database.Secrets.UpdateAsync(secret, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Replace the supplied field values. Omitted fields are kept, the mask placeholder keeps the stored value, and
        /// fields in Remove are deleted. Required fields cannot be removed.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Secret identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated secret, and the names of the fields that changed.</returns>
        /// <exception cref="NotFoundException">Thrown when the secret does not exist in the tenant.</exception>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="ConflictException">Thrown when the secret is protected.</exception>
        public async Task<SecretValueUpdate> UpdateValuesAsync(string tenantId, string id, SecretValueRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Secret secret = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (secret.IsProtected) throw new ConflictException("Protected", "Secret " + secret.Name + " is protected.");

            Dictionary<string, string> supplied = request.Values ?? new Dictionary<string, string>();
            Dictionary<string, string> changed = supplied.Where(v => v.Value != SecretFieldRules.MaskPlaceholder).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
            List<string> remove = request.Remove ?? new List<string>();

            Dictionary<string, string> errors = new Dictionary<string, string>();
            Dictionary<string, string> codes = new Dictionary<string, string>();
            foreach (string field in supplied.Keys.Where(k => supplied[k] == SecretFieldRules.MaskPlaceholder))
            {
                if (!secret.Values.Any(v => v.FieldName == field))
                {
                    errors["values." + field] = "The field has no stored value to keep.";
                    codes["values." + field] = "NoStoredValue";
                }
            }
            ValidateValues(secret.Kind, changed, errors, codes, false);
            foreach (string field in remove)
            {
                if (SecretFieldRules.Required(secret.Kind).Contains(field, StringComparer.Ordinal))
                {
                    errors["remove." + field] = "A required field cannot be removed.";
                    codes["remove." + field] = "RequiredField";
                }
            }
            int finalCount = secret.Values.Select(v => v.FieldName).Union(changed.Keys).Except(remove).Count();
            if (finalCount > SecretFieldRules.MaxFields)
            {
                errors["values"] = "A secret holds at most " + SecretFieldRules.MaxFields + " fields.";
                codes["values"] = "TooManyFields";
            }
            if (finalCount == 0)
            {
                errors["values"] = "A secret must keep at least one field value.";
                codes["values"] = "RequiredFieldMissing";
            }
            if (errors.Count > 0) throw new ValidationException("The secret values are invalid: " + String.Join(" ", errors.Values), errors, codes);

            List<SecretValue> values = secret.Values.Where(v => !remove.Contains(v.FieldName, StringComparer.Ordinal) && !changed.ContainsKey(v.FieldName)).ToList();
            foreach (KeyValuePair<string, string> field in changed) values.Add(Encrypt(secret, field.Key, field.Value));
            secret.Values = values;

            Dictionary<string, string> plaintext = DecryptAll(secret);
            Finish(secret, plaintext);
            Secret updated = await _Database.Secrets.UpdateAsync(secret, token).ConfigureAwait(false);
            return new SecretValueUpdate(updated, changed.Keys.Concat(remove.Where(r => secret.Values.All(v => v.FieldName != r))).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList());
        }

        /// <summary>
        /// Find records referencing a secret.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Referrers (empty when none).</returns>
        /// <exception cref="NotFoundException">Thrown when the secret does not exist in the tenant.</exception>
        public async Task<List<SecretReferrer>> ReferrersAsync(string tenantId, string id, CancellationToken token = default)
        {
            Secret secret = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            List<SecretReferrer> referrers = new List<SecretReferrer>();
            foreach (ISecretReferrerProvider provider in _Providers)
            {
                referrers.AddRange(await provider.FindAsync(tenantId, secret.Id, token).ConfigureAwait(false));
            }
            return referrers.OrderBy(r => r.ReferrerType, StringComparer.Ordinal).ThenBy(r => r.ReferrerName, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Delete a secret that nothing references.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted secret.</returns>
        /// <exception cref="NotFoundException">Thrown when the secret does not exist in the tenant.</exception>
        /// <exception cref="ConflictException">Thrown when the secret is protected or referenced.</exception>
        public async Task<Secret> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            Secret secret = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (secret.IsProtected) throw new ConflictException("Protected", "Secret " + secret.Name + " is protected.");
            List<SecretReferrer> referrers = await ReferrersAsync(tenantId, id, token).ConfigureAwait(false);
            if (referrers.Count > 0)
            {
                throw new ConflictException("SecretInUse", "Secret " + secret.Name + " is used by " + String.Join(", ", referrers.Select(r => r.ReferrerType + " " + (r.ReferrerName ?? r.ReferrerId))) + ".");
            }
            await _Database.Secrets.DeleteAsync(secret.Id, token).ConfigureAwait(false);
            return secret;
        }

        /// <summary>
        /// Decrypt every field of a secret for an authorized reveal, and record the reveal time. The caller is
        /// responsible for authorization, re-authentication, the reason, and the audit record.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The secret and its plaintext values.</returns>
        /// <exception cref="NotFoundException">Thrown when the secret does not exist in the tenant.</exception>
        /// <exception cref="System.Security.Cryptography.CryptographicException">Thrown when a value cannot be decrypted.</exception>
        public async Task<SecretReveal> RevealAsync(string tenantId, string id, CancellationToken token = default)
        {
            Secret secret = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            Dictionary<string, string> values;
            try
            {
                values = DecryptAll(secret);
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException || e is FormatException)
            {
                HugoMetrics.Count(HugoMetricCatalog.SecretOperations, "operation", "reveal", "outcome", "error");
                throw;
            }
            secret.LastRevealedUtc = DateTime.UtcNow;
            await _Database.Secrets.UpdateAsync(secret, token).ConfigureAwait(false);
            HugoMetrics.Count(HugoMetricCatalog.SecretOperations, "operation", "reveal", "outcome", "ok");
            return new SecretReveal(secret, values);
        }

        /// <summary>
        /// Decrypt every field of a secret for internal use (connectors, model endpoints). Not audited as a reveal; the
        /// caller registers the values with its scrubber.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Plaintext values by field name.</returns>
        /// <exception cref="NotFoundException">Thrown when the secret does not exist in the tenant.</exception>
        public async Task<Dictionary<string, string>> ResolveAsync(string tenantId, string id, CancellationToken token = default)
        {
            Secret secret = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            return DecryptAll(secret);
        }

        #endregion

        #region Private-Methods

        private SecretValue Encrypt(Secret secret, string fieldName, string plaintext)
        {
            SecretValue value = new SecretValue();
            value.SecretId = secret.Id;
            value.TenantId = secret.TenantId;
            value.FieldName = fieldName;
            value.Ciphertext = _Cipher.Encrypt(Constants.KeyPurposeSecrets, plaintext, SecretCipher.SecretFieldAssociatedData(secret.TenantId, secret.Id, fieldName));
            value.KeyVersion = _Cipher.KeyVersion;
            return value;
        }

        private Dictionary<string, string> DecryptAll(Secret secret)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SecretValue value in secret.Values)
            {
                result[value.FieldName] = _Cipher.Decrypt(Constants.KeyPurposeSecrets, value.Ciphertext, SecretCipher.SecretFieldAssociatedData(secret.TenantId, secret.Id, value.FieldName));
            }
            return result;
        }

        private void Finish(Secret secret, Dictionary<string, string> plaintext)
        {
            secret.KeyVersion = secret.Values.Count > 0 ? secret.Values.Min(v => v.KeyVersion) : _Cipher.KeyVersion;
            secret.LastRotatedUtc = DateTime.UtcNow;
            secret.Fingerprint = ComputeFingerprint(secret.Kind, plaintext);
        }

        private static string? ComputeFingerprint(SecretKindEnum kind, Dictionary<string, string> values)
        {
            try
            {
                if (kind == SecretKindEnum.PrivateKey && values.TryGetValue("privateKey", out string? key))
                {
                    values.TryGetValue("passphrase", out string? passphrase);
                    string? fingerprint = SshKeyInspector.Inspect(key, passphrase).Fingerprint;
                    if (!String.IsNullOrEmpty(fingerprint)) return fingerprint;
                }
                if (kind == SecretKindEnum.Certificate && values.TryGetValue("certificate", out string? certificate))
                {
                    string? fingerprint = SecretImporter.ParseCertificate(certificate, null).Fingerprint;
                    if (!String.IsNullOrEmpty(fingerprint)) return fingerprint;
                }
            }
            catch (ArgumentException)
            {
            }
            string? primary = SecretFieldRules.PrimaryField(kind);
            if (primary == null || !values.TryGetValue(primary, out string? text) || String.IsNullOrEmpty(text)) return null;
            if (text.Length < 12) return "****";
            return "****" + SecretGenerator.Last4(text.TrimEnd());
        }

        private static void ValidateMetadata(SecretRequest request, Dictionary<string, string> errors, Dictionary<string, string> codes, bool create)
        {
            string name = (request.Name ?? String.Empty).Trim();
            if (name.Length == 0 && (create || request.Name != null))
            {
                errors["name"] = "A name is required.";
                codes["name"] = "Required";
            }
            else if (name.Length > 256)
            {
                errors["name"] = "The name is longer than 256 characters.";
                codes["name"] = "TooLong";
            }
            if (request.Description != null && request.Description.Length > 4096)
            {
                errors["description"] = "The description is longer than 4096 characters.";
                codes["description"] = "TooLong";
            }
            if (create && request.ExpiresUtc.HasValue && request.ExpiresUtc.Value.ToUniversalTime() < DateTime.UtcNow)
            {
                errors["expiresUtc"] = "The expiry is in the past.";
                codes["expiresUtc"] = "ExpiryInPast";
            }
        }

        private static void ValidateValues(SecretKindEnum kind, Dictionary<string, string> values, Dictionary<string, string> errors, Dictionary<string, string> codes, bool create)
        {
            if (values.Count > SecretFieldRules.MaxFields)
            {
                errors["values"] = "A secret holds at most " + SecretFieldRules.MaxFields + " fields.";
                codes["values"] = "TooManyFields";
            }
            foreach (KeyValuePair<string, string> field in values)
            {
                string path = "values." + field.Key;
                if (!SecretFieldRules.Accepts(kind, field.Key))
                {
                    errors[path] = "The field is not accepted by a " + kind + " secret.";
                    codes[path] = "UnknownField";
                }
                else if (String.IsNullOrEmpty(field.Value))
                {
                    errors[path] = "The value is empty.";
                    codes[path] = "Required";
                }
                else if (kind == SecretKindEnum.AesKey && (field.Key == "keyEncoding" || field.Key == "saltEncoding" || field.Key == "ivEncoding") && field.Value != "base64" && field.Value != "hex")
                {
                    errors[path] = "Select base64 or hex for AES material encoding.";
                    codes[path] = "InvalidValue";
                }
                else if (create && field.Value == SecretFieldRules.MaskPlaceholder)
                {
                    errors[path] = "The mask placeholder is not a value.";
                    codes[path] = "MaskPlaceholder";
                }
                else if (field.Value.Length > SecretFieldRules.MaxValueLength)
                {
                    errors[path] = "Values are limited to 1 MiB.";
                    codes[path] = "TooLong";
                }
            }
        }

        #endregion
    }
}
