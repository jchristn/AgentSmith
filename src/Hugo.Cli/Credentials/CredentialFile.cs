namespace Hugo.Cli.Credentials
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Contents of the credential file. Not thread-safe.
    /// </summary>
    public class CredentialFile
    {
        #region Public-Members

        /// <summary>
        /// File format version. Currently 1.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// The server of the most recent sign-in, used when no server is given, or null.
        /// </summary>
        public string? DefaultServer { get; set; } = null;

        /// <summary>
        /// One entry per server. Never null.
        /// </summary>
        public List<ServerCredential> Servers
        {
            get
            {
                return _Servers;
            }
            set
            {
                _Servers = value ?? new List<ServerCredential>();
            }
        }

        #endregion

        #region Private-Members

        private List<ServerCredential> _Servers = new List<ServerCredential>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The entry for a server, or null.
        /// </summary>
        /// <param name="server">Normalized server URL.</param>
        /// <returns>Entry or null.</returns>
        public ServerCredential? Find(string server)
        {
            if (String.IsNullOrEmpty(server)) return null;
            return Servers.FirstOrDefault(s => String.Equals(s.Server, server, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Add or replace the entry for its server and make that server the default.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <exception cref="ArgumentNullException">Thrown when the entry is null or has no server.</exception>
        public void Upsert(ServerCredential entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(entry.Server)) throw new ArgumentNullException(nameof(entry.Server));
            Servers.RemoveAll(s => String.Equals(s.Server, entry.Server, StringComparison.OrdinalIgnoreCase));
            Servers.Add(entry);
            DefaultServer = entry.Server;
        }

        /// <summary>
        /// Remove the entry for a server. When it was the default, the most recently saved remaining entry becomes the
        /// default.
        /// </summary>
        /// <param name="server">Normalized server URL.</param>
        /// <returns>True when an entry was removed.</returns>
        public bool Remove(string server)
        {
            int removed = Servers.RemoveAll(s => String.Equals(s.Server, server, StringComparison.OrdinalIgnoreCase));
            if (String.Equals(DefaultServer, server, StringComparison.OrdinalIgnoreCase))
            {
                ServerCredential? latest = Servers.OrderByDescending(s => s.SavedUtc).FirstOrDefault();
                DefaultServer = latest?.Server;
            }
            return removed > 0;
        }

        #endregion
    }
}
