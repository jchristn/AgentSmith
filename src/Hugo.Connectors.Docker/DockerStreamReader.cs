namespace Hugo.Connectors.Docker
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet;

    /// <summary>
    /// Reads a Docker multiplexed stream (logs, exec output) into bounded stdout and stderr buffers. Thread-safe.
    /// </summary>
    public static class DockerStreamReader
    {
        #region Public-Methods

        /// <summary>
        /// Read until the stream ends.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="stdout">Buffer for stdout.</param>
        /// <param name="stderr">Buffer for stderr.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task ReadAsync(MultiplexedStream stream, DockerTailBuffer stdout, DockerTailBuffer stderr, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(stdout);
            ArgumentNullException.ThrowIfNull(stderr);
            byte[] buffer = new byte[16384];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                MultiplexedStream.ReadResult read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (read.EOF || read.Count <= 0) return;
                if (read.Target == MultiplexedStream.TargetStream.StandardError) stderr.Write(buffer, 0, read.Count);
                else stdout.Write(buffer, 0, read.Count);
            }
        }

        #endregion
    }
}
