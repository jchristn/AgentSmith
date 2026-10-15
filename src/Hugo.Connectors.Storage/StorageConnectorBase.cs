namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Shared implementation of the three object storage connectors: common settings, the shared tool set, common
    /// validation, and the connection test. Thread-safe.
    /// </summary>
    public abstract class StorageConnectorBase : IConnector
    {
        #region Public-Members

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Provider.
        /// </summary>
        public StorageProviderEnum Provider { get; }

        /// <summary>
        /// Time limit for <see cref="TestConnectionAsync"/>. Minimum 1 second, maximum 5 minutes, default 30 seconds.
        /// </summary>
        public TimeSpan TestTimeout
        {
            get
            {
                return _TestTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(5)) value = TimeSpan.FromMinutes(5);
                _TestTimeout = value;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _TestTimeout = TimeSpan.FromSeconds(30);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. Appends the common settings to the descriptor's schema and creates the shared tools.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="descriptor">Descriptor with the provider-specific settings and secret slots.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected StorageConnectorBase(StorageProviderEnum provider, ConnectorDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            Provider = provider;
            descriptor.Family = "storage";
            descriptor.Category = ConnectorCategoryEnum.Storage;
            descriptor.IconKey = "bucket";
            AddCommonSettings(descriptor.SettingsSchema);
            Descriptor = descriptor;
            Tools = StorageToolSet.Create(provider);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            ConnectorValidationResult result = new ConnectorValidationResult();

            string restriction = (resource.Settings.GetString(StorageSettingKeys.PrefixRestriction) ?? String.Empty).Trim();
            if (restriction.Length > 0)
            {
                string? problem = StorageKeyPolicy.Describe(restriction, true);
                if (problem != null) result.Add(StorageSettingKeys.PrefixRestriction, "invalid_format", "The prefix restriction " + problem + ".");
            }

            ValidateProvider(resource, result);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch stopwatch = Stopwatch.StartNew();

            ConnectorValidationResult validation = await ValidateAsync(resource, token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                ConnectorValidationError first = validation.Errors[0];
                return ConnectorHealthResult.Fail("invalid_configuration", first.Field + ": " + first.Message, 0);
            }

            StorageSession? session = null;
            string container = String.Empty;
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(_TestTimeout);
                try
                {
                    StorageOptions options = StorageOptions.FromSettings(resource.Settings);
                    session = StorageClientFactory.CreateSession(Provider, resource);
                    container = session.ContainerName;

                    string? sample = null;
                    EnumerationFilter filter = new EnumerationFilter();
                    filter.Prefix = options.PrefixRestriction;
                    await foreach (BlobMetadata md in session.Client.EnumerateAsync(filter, cts.Token).ConfigureAwait(false))
                    {
                        if (md == null) continue;
                        sample = md.Key;
                        break;
                    }
                    cts.Token.ThrowIfCancellationRequested();

                    double latency = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
                    string where = Descriptor.DisplayName + " " + (Provider == StorageProviderEnum.AzureBlob ? "container" : "bucket") + " '" + container + "'";
                    string message = sample == null
                        ? "Listed " + where + "; no objects under '" + options.PrefixRestriction + "'."
                        : "Listed " + where + "; first object '" + sample + "'.";
                    ConnectorHealthResult ok = ConnectorHealthResult.Ok(message, latency);
                    ok.Details["provider"] = Descriptor.Type;
                    ok.Details["container"] = container;
                    if (options.PrefixRestriction.Length > 0) ok.Details["prefixRestriction"] = options.PrefixRestriction;
                    if (sample != null) ok.Details["sampleKey"] = sample;
                    ok.Details["latencyMs"] = latency.ToString(CultureInfo.InvariantCulture);
                    return ok;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail("timeout", "Listing timed out after " + _TestTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " seconds.", stopwatch.Elapsed.TotalMilliseconds);
                }
                catch (ConnectorException e)
                {
                    return ConnectorHealthResult.Fail(e.Code, e.Message, stopwatch.Elapsed.TotalMilliseconds);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    ConnectorException mapped = StorageErrors.Map(e, container, null);
                    return ConnectorHealthResult.Fail(mapped.Code, mapped.Message, stopwatch.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    if (session != null) session.Dispose();
                }
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Provider-specific cross-field validation.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="result">Result to add errors to.</param>
        protected abstract void ValidateProvider(ConnectorResourceContext resource, ConnectorValidationResult result);

        /// <summary>
        /// Validate an optional endpoint URL setting.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="key">Setting key.</param>
        /// <param name="result">Result.</param>
        protected static void ValidateEndpoint(ConnectorResourceContext resource, string key, ConnectorValidationResult result)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(result);
            try
            {
                StorageClientFactory.ParseEndpoint(resource.Settings.GetString(key), true, key);
            }
            catch (ConnectorException e)
            {
                result.Add(key, "invalid_format", e.Message);
            }
        }

        /// <summary>
        /// Read the embedded default instructions.
        /// </summary>
        /// <param name="fileName">Resource file name, for example <c>connector.s3.instructions.md</c>.</param>
        /// <returns>Text.</returns>
        protected static string ReadInstructions(string fileName)
        {
            return EmbeddedText.Read(typeof(StorageConnectorBase).Assembly, fileName);
        }

        #endregion

        #region Private-Methods

        private static void AddCommonSettings(ConnectorSettingsSchema schema)
        {
            schema.Add(new SettingDescriptor(StorageSettingKeys.PrefixRestriction, SettingValueKindEnum.String, "Prefix restriction")
            {
                Help = "When set, every key the agent lists, reads, writes, copies, or deletes must start with this prefix (for example 'reports/'). Include the trailing '/' to restrict to a folder. Empty means the whole bucket or container.",
                Group = "Safety",
                ShowInSummary = true
            });
            schema.Add(new SettingDescriptor(StorageSettingKeys.MaxObjectReadBytes, SettingValueKindEnum.Integer, "Maximum bytes per read")
            {
                Help = "Largest amount of object content storage_read returns in one call; larger objects are read in ranges. Default 1048576 (1 MiB), minimum 1024, maximum 16777216.",
                DefaultValue = StorageOptions.DefaultReadBytes.ToString(CultureInfo.InvariantCulture),
                Minimum = StorageOptions.MinimumReadBytes,
                Maximum = StorageOptions.MaximumReadBytes,
                Group = "Limits"
            });
            schema.Add(new SettingDescriptor(StorageSettingKeys.MaxListResults, SettingValueKindEnum.Integer, "Maximum list results")
            {
                Help = "Largest page storage_list returns. Default 1000, minimum 1, maximum 10000. The call's output row limit also applies.",
                DefaultValue = StorageOptions.DefaultListResults.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = StorageOptions.MaximumListResults,
                Group = "Limits"
            });
            schema.Add(new SettingDescriptor(StorageSettingKeys.MaxScanObjects, SettingValueKindEnum.Integer, "Maximum objects scanned")
            {
                Help = "Most objects storage_stats examines in one call, and storage_list examines per page past its continuation point, before reporting a truncated result. A storage_list page may also re-walk up to ten times this many keys that earlier pages returned. Default 100000, minimum 100, maximum 10000000.",
                DefaultValue = StorageOptions.DefaultScanObjects.ToString(CultureInfo.InvariantCulture),
                Minimum = StorageOptions.MinimumScanObjects,
                Maximum = StorageOptions.MaximumScanObjects,
                Group = "Limits"
            });
        }

        #endregion
    }
}
