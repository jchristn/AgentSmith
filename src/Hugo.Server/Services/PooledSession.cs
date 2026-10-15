namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// One pooled connector session with its last use time. Not thread-safe on its own; the pool guards it.
    /// </summary>
    public class PooledSession
    {
        /// <summary>
        /// The session (an SSH client, a database connection, and so on).
        /// </summary>
        public IDisposable Session { get; set; }

        /// <summary>
        /// Last time the session was handed out (UTC).
        /// </summary>
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <exception cref="ArgumentNullException">Thrown when session is null.</exception>
        public PooledSession(IDisposable session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }
    }
}
