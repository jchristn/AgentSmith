namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds one page of a <c>storage_list</c> listing from a flat, lexicographically ordered enumeration. Blobject cannot
    /// resume an enumeration from a marker, so a page after the first re-walks the listing from the start of the prefix
    /// and skips every key up to the last key of the previous page (carried in the continuation token). Skipped keys are
    /// compared and dropped without further work, and they do not count toward <see cref="MaxScanObjects"/>; they count
    /// toward <see cref="MaxSkipObjects"/> instead. Not thread-safe; create one per call.
    /// </summary>
    public class StorageListPager
    {
        #region Public-Members

        /// <summary>
        /// Most keys this page may examine after the continuation point (returned entries plus keys folded into
        /// folders). Default 100000, minimum 1.
        /// </summary>
        public long MaxScanObjects
        {
            get
            {
                return _MaxScanObjects;
            }
            set
            {
                _MaxScanObjects = Math.Max(1, value);
            }
        }

        /// <summary>
        /// Most keys this page may skip while re-walking up to the continuation point. Default 1000000, minimum 1.
        /// </summary>
        public long MaxSkipObjects
        {
            get
            {
                return _MaxSkipObjects;
            }
            set
            {
                _MaxSkipObjects = Math.Max(1, value);
            }
        }

        /// <summary>
        /// Most entries to return. Default 1000, minimum 1.
        /// </summary>
        public int MaxResults
        {
            get
            {
                return _MaxResults;
            }
            set
            {
                _MaxResults = Math.Max(1, value);
            }
        }

        #endregion

        #region Private-Members

        private long _MaxScanObjects = 100000;
        private long _MaxSkipObjects = 1000000;
        private int _MaxResults = 1000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build one page.
        /// </summary>
        /// <param name="source">Enumeration of the objects under the prefix, in key order.</param>
        /// <param name="prefix">Resolved prefix. Keys outside it are ignored.</param>
        /// <param name="delimiter">Folder delimiter, or null for a flat listing.</param>
        /// <param name="startAfter">Last key of the previous page, or null for the first page.</param>
        /// <param name="toInfo">Converts an object to an entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page, with <c>ContinuationToken</c> set when more entries may exist.</returns>
        /// <exception cref="ArgumentNullException">Thrown when source, prefix, or toInfo is null.</exception>
        /// <exception cref="ConnectorException">Thrown with code <c>scan_limit</c> when the skip budget runs out, or when
        /// the scan budget runs out before any entry was found.</exception>
        public async Task<StorageListResult> PageAsync(
            IAsyncEnumerable<BlobMetadata> source,
            string prefix,
            string? delimiter,
            string? startAfter,
            Func<BlobMetadata, StorageObjectInfo> toInfo,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(prefix);
            ArgumentNullException.ThrowIfNull(toInfo);

            StorageListResult result = new StorageListResult();
            result.Prefix = prefix;
            result.Delimiter = delimiter;

            string? lastFolder = null;
            bool hasMore = false;
            long scanned = 0;
            long skipped = 0;
            bool skipFolder = startAfter != null && delimiter != null && startAfter.EndsWith(delimiter, StringComparison.Ordinal);

            await foreach (BlobMetadata md in source.WithCancellation(token).ConfigureAwait(false))
            {
                if (md == null || String.IsNullOrEmpty(md.Key)) continue;
                string key = md.Key;
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;

                if (startAfter != null
                    && (String.CompareOrdinal(key, startAfter) <= 0 || (skipFolder && key.StartsWith(startAfter, StringComparison.Ordinal))))
                {
                    skipped++;
                    if (skipped > _MaxSkipObjects)
                        throw new ConnectorException(
                            "scan_limit",
                            "Re-walked " + _MaxSkipObjects + " keys already returned by earlier pages without reaching the continuation point. "
                            + "Object storage listings cannot resume from a marker here, so deep paging under one prefix is bounded; list a longer, more specific prefix instead.");
                    continue;
                }

                if (scanned >= _MaxScanObjects)
                {
                    result.ScanTruncated = true;
                    hasMore = true;
                    break;
                }
                scanned++;

                StorageObjectInfo? entry = null;
                if (delimiter != null)
                {
                    int index = key.IndexOf(delimiter, prefix.Length, StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        string folder = key.Substring(0, index + delimiter.Length);
                        if (folder != key || md.ContentLength == 0)
                        {
                            if (folder == lastFolder) continue;
                            lastFolder = folder;
                            entry = new StorageObjectInfo { Key = folder, IsFolder = true };
                        }
                    }
                }
                if (entry == null) entry = toInfo(md);

                if (result.Entries.Count >= _MaxResults)
                {
                    hasMore = true;
                    break;
                }

                result.Entries.Add(entry);
            }

            token.ThrowIfCancellationRequested();
            if (result.ScanTruncated && result.Entries.Count == 0)
                throw new ConnectorException(
                    "scan_limit",
                    "Scanned " + scanned + " objects (the resource's maxScanObjects) without reaching new entries. Use a longer, more specific prefix.");

            result.Scanned = scanned;
            result.Skipped = skipped;
            foreach (StorageObjectInfo e in result.Entries)
            {
                if (e.IsFolder) result.FolderCount++;
                else result.ObjectCount++;
            }
            if (hasMore && result.Entries.Count > 0) result.ContinuationToken = StorageContinuationToken.Encode(result.Entries[result.Entries.Count - 1].Key);
            return result;
        }

        #endregion
    }
}
