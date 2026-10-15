namespace Hugo.Cli.Credentials
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads and writes the per-user credential file (default <c>~/.hugo/credentials.json</c>). On Linux and macOS the
    /// directory is created with mode 0700 and the file is written with mode 0600 (owner read and write only), by
    /// writing a temporary file with that mode and renaming it over the old one. On Windows the file inherits the
    /// user profile's ACL. Thread-safe within one process.
    /// </summary>
    public class CredentialStore
    {
        #region Public-Members

        /// <summary>
        /// Full path of the credential file. Paths with spaces are supported.
        /// </summary>
        public string FilePath { get; }

        #endregion

        #region Private-Members

        private static readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private const UnixFileMode _FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private const UnixFileMode _DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="filePath">Full path of the credential file.</param>
        /// <exception cref="ArgumentNullException">Thrown when the path is null or empty.</exception>
        public CredentialStore(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            FilePath = Path.GetFullPath(filePath);
        }

        /// <summary>
        /// The default credential file path for a home directory: <c>{home}/.hugo/credentials.json</c>.
        /// </summary>
        /// <param name="homeDirectory">Home directory.</param>
        /// <returns>Path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the home directory is null or empty.</exception>
        public static string DefaultPath(string homeDirectory)
        {
            if (String.IsNullOrEmpty(homeDirectory)) throw new ArgumentNullException(nameof(homeDirectory));
            return Path.Combine(homeDirectory, ".hugo", "credentials.json");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the file. A missing file reads as an empty credential file.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Contents.</returns>
        /// <exception cref="CliException">Thrown when the file exists but cannot be read or is not valid.</exception>
        public async Task<CredentialFile> LoadAsync(CancellationToken token)
        {
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await ReadAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// Write the file with owner-only permissions.
        /// </summary>
        /// <param name="file">Contents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when file is null.</exception>
        /// <exception cref="CliException">Thrown when the file cannot be written.</exception>
        public async Task SaveAsync(CredentialFile file, CancellationToken token)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await WriteAsync(file, token).ConfigureAwait(false);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// Read, change, and write the file under one lock.
        /// </summary>
        /// <param name="change">Change to apply; return false to skip writing.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the file was written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when change is null.</exception>
        /// <exception cref="CliException">Thrown when the file cannot be read or written.</exception>
        public async Task<bool> UpdateAsync(Func<CredentialFile, bool> change, CancellationToken token)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                CredentialFile file = await ReadAsync(token).ConfigureAwait(false);
                if (!change(file)) return false;
                await WriteAsync(file, token).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _Lock.Release();
            }
        }

        #endregion

        #region Private-Methods

        private async Task<CredentialFile> ReadAsync(CancellationToken token)
        {
            if (!File.Exists(FilePath)) return new CredentialFile();
            string json;
            try
            {
                json = await File.ReadAllTextAsync(FilePath, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new CliException("Cannot read the credential file " + FilePath + ": " + e.Message, e);
            }
            if (String.IsNullOrWhiteSpace(json)) return new CredentialFile();
            try
            {
                CredentialFile? file = JsonSerializer.Deserialize<CredentialFile>(json, CliJson.Options);
                return file ?? new CredentialFile();
            }
            catch (JsonException e)
            {
                throw new CliException("The credential file " + FilePath + " is not valid JSON. Delete it and run 'hugo login' again.", e);
            }
        }

        private async Task WriteAsync(CredentialFile file, CancellationToken token)
        {
            string json = JsonSerializer.Serialize(file, CliJson.Indented);
            string? directory = Path.GetDirectoryName(FilePath);
            string temp = FilePath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
                    else Directory.CreateDirectory(directory, _DirectoryMode);
                }

                FileStreamOptions options = new FileStreamOptions();
                options.Mode = FileMode.CreateNew;
                options.Access = FileAccess.Write;
                options.Share = FileShare.None;
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = _FileMode;
                using (FileStream stream = new FileStream(temp, options))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                }
                File.Move(temp, FilePath, true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(FilePath, _FileMode);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                TryDelete(temp);
                throw new CliException("Cannot write the credential file " + FilePath + ": " + e.Message, e);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
