namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Re-encrypts secret values and credential secret keys under the current master key version in batches (P2.10).
    /// The job is resumable: it only touches rows whose key version differs from the current one, so a run that stops
    /// part way is finished by the next. One run at a time. Thread-safe.
    /// </summary>
    public class MasterKeyRotationService
    {
        #region Public-Members

        /// <summary>
        /// Rows per batch (one transaction each). Minimum 1, maximum 5000, default 200.
        /// </summary>
        public int BatchSize
        {
            get
            {
                return _BatchSize;
            }
            set
            {
                _BatchSize = Math.Clamp(value, 1, 5000);
            }
        }

        /// <summary>
        /// Raised after each batch with a copy of the status (for the event hub and logs).
        /// </summary>
        public event EventHandler<MasterKeyRotationStatus>? Progress;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly SecretCipher _Cipher;
        private readonly object _Lock = new object();
        private readonly MasterKeyRotationStatus _Status = new MasterKeyRotationStatus();
        private int _BatchSize = 200;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="cipher">Cipher holding the current and previous keys.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public MasterKeyRotationService(DatabaseDriverBase database, SecretCipher cipher)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Current status, with the remaining count refreshed from the database.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status copy.</returns>
        public async Task<MasterKeyRotationStatus> StatusAsync(CancellationToken token = default)
        {
            long remaining = await _Database.Secrets.CountValuesNotAtVersionAsync(_Cipher.KeyVersion, token).ConfigureAwait(false);
            lock (_Lock)
            {
                _Status.Remaining = remaining;
                _Status.TargetKeyVersion = _Cipher.KeyVersion;
                return Copy();
            }
        }

        /// <summary>
        /// Start a rotation in the background unless one is running.
        /// </summary>
        /// <param name="token">Cancellation token for the background run.</param>
        /// <returns>True when a run started; false when one was already running.</returns>
        public bool TryStart(CancellationToken token)
        {
            lock (_Lock)
            {
                if (_Status.Running) return false;
                _Status.Running = true;
                _Status.TargetKeyVersion = _Cipher.KeyVersion;
                _Status.Total = 0;
                _Status.Completed = 0;
                _Status.Failed = 0;
                _Status.LastError = null;
                _Status.StartedUtc = DateTime.UtcNow;
                _Status.CompletedUtc = null;
            }
            _ = Task.Run(() => RunGuardedAsync(token));
            return true;
        }

        /// <summary>
        /// Run a rotation to completion on the calling task (tests and maintenance tools).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Final status.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a run is already in progress.</exception>
        public async Task<MasterKeyRotationStatus> RunAsync(CancellationToken token = default)
        {
            lock (_Lock)
            {
                if (_Status.Running) throw new InvalidOperationException("A master key rotation is already running.");
                _Status.Running = true;
                _Status.TargetKeyVersion = _Cipher.KeyVersion;
                _Status.Total = 0;
                _Status.Completed = 0;
                _Status.Failed = 0;
                _Status.LastError = null;
                _Status.StartedUtc = DateTime.UtcNow;
                _Status.CompletedUtc = null;
            }
            await RunGuardedAsync(token).ConfigureAwait(false);
            return await StatusAsync(token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task RunGuardedAsync(CancellationToken token)
        {
            long start = Stopwatch.GetTimestamp();
            string outcome = "ok";
            try
            {
                await RunCoreAsync(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                outcome = "error";
                lock (_Lock)
                {
                    _Status.LastError = e.Message;
                }
            }
            finally
            {
                long remaining = 0;
                try
                {
                    remaining = await _Database.Secrets.CountValuesNotAtVersionAsync(_Cipher.KeyVersion, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    remaining = -1;
                }
                lock (_Lock)
                {
                    _Status.Running = false;
                    _Status.Remaining = remaining;
                    _Status.CompletedUtc = DateTime.UtcNow;
                }
                HugoMetrics.Count(HugoMetricCatalog.SecretOperations, "operation", "rotate", "outcome", outcome);
                HugoMetrics.Count(HugoMetricCatalog.BackgroundJobs, "job", "master_key_rotation", "outcome", outcome);
                HugoMetrics.Observe(HugoMetricCatalog.BackgroundJobDuration, Stopwatch.GetElapsedTime(start).TotalSeconds, "job", "master_key_rotation");
                Raise();
            }
        }

        private async Task RunCoreAsync(CancellationToken token)
        {
            int version = _Cipher.KeyVersion;
            long pendingValues = await _Database.Secrets.CountValuesNotAtVersionAsync(version, token).ConfigureAwait(false);
            lock (_Lock)
            {
                _Status.Total = pendingValues;
            }

            // Secret values: each batch reads rows still under another version; rows that fail to decrypt are skipped and
            // counted, and the loop stops when a batch makes no progress.
            HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);
            while (!token.IsCancellationRequested)
            {
                List<SecretValue> batch = await _Database.Secrets.ReadValuesNotAtVersionAsync(version, _BatchSize + failed.Count, token).ConfigureAwait(false);
                List<SecretValue> updates = new List<SecretValue>();
                foreach (SecretValue value in batch)
                {
                    if (failed.Contains(value.Id)) continue;
                    string aad = SecretCipher.SecretFieldAssociatedData(value.TenantId, value.SecretId, value.FieldName);
                    try
                    {
                        value.Ciphertext = _Cipher.Reencrypt(Constants.KeyPurposeSecrets, value.Ciphertext, aad);
                        value.KeyVersion = version;
                        updates.Add(value);
                    }
                    catch (Exception e) when (e is CryptographicException || e is FormatException)
                    {
                        failed.Add(value.Id);
                        lock (_Lock)
                        {
                            _Status.Failed++;
                            _Status.LastError = "Secret value " + value.Id + " could not be decrypted with any configured key.";
                        }
                    }
                    if (updates.Count >= _BatchSize) break;
                }
                if (updates.Count == 0) break;
                await _Database.Secrets.UpdateCiphertextsAsync(updates, token).ConfigureAwait(false);
                lock (_Lock)
                {
                    _Status.Completed += updates.Count;
                }
                Raise();
            }

            // Credential secret keys carry their key version in the frame.
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = Math.Min(1000, _BatchSize);
            query.Ordering = Hugo.Core.Enums.EnumerationOrderEnum.CreatedAscending;
            int skip = 0;
            while (!token.IsCancellationRequested)
            {
                query.Skip = skip;
                EnumerationResult<Credential> page = await _Database.Credentials.EnumerateAsync(null, query, token).ConfigureAwait(false);
                foreach (Credential credential in page.Objects)
                {
                    if (SecretCipher.ReadKeyVersion(credential.SecretKeyEncrypted) == version) continue;
                    lock (_Lock)
                    {
                        _Status.Total++;
                    }
                    try
                    {
                        credential.SecretKeyEncrypted = _Cipher.Reencrypt(Constants.KeyPurposeCredentials, credential.SecretKeyEncrypted, credential.Id);
                        await _Database.Credentials.UpdateAsync(credential, token).ConfigureAwait(false);
                        lock (_Lock)
                        {
                            _Status.Completed++;
                        }
                    }
                    catch (Exception e) when (e is CryptographicException || e is FormatException || e is ArgumentException)
                    {
                        lock (_Lock)
                        {
                            _Status.Failed++;
                            _Status.LastError = "Credential " + credential.Id + " could not be decrypted with any configured key.";
                        }
                    }
                }
                if (page.Objects.Count < query.MaxResults) break;
                skip += page.Objects.Count;
                Raise();
            }
        }

        private void Raise()
        {
            MasterKeyRotationStatus copy;
            lock (_Lock)
            {
                copy = Copy();
            }
            try
            {
                Progress?.Invoke(this, copy);
            }
            catch (Exception)
            {
                // A failing listener must not stop the rotation.
            }
        }

        private MasterKeyRotationStatus Copy()
        {
            MasterKeyRotationStatus copy = new MasterKeyRotationStatus();
            copy.Running = _Status.Running;
            copy.TargetKeyVersion = _Status.TargetKeyVersion;
            copy.Total = _Status.Total;
            copy.Completed = _Status.Completed;
            copy.Failed = _Status.Failed;
            copy.Remaining = _Status.Remaining;
            copy.StartedUtc = _Status.StartedUtc;
            copy.CompletedUtc = _Status.CompletedUtc;
            copy.LastError = _Status.LastError;
            return copy;
        }

        #endregion
    }
}
