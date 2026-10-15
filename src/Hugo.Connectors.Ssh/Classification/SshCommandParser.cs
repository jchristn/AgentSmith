namespace Hugo.Connectors.Ssh.Classification
{
    /// <summary>
    /// Parses POSIX shell command lines into simple command segments for classification. Stateless and thread-safe.
    /// </summary>
    public static class SshCommandParser
    {
        /// <summary>
        /// Parse a command line. Constructs that cannot be analyzed reliably (here-documents, function definitions,
        /// case statements, unbalanced quotes) return a failed result so callers can fail closed.
        /// </summary>
        /// <param name="command">Command line. Null is treated as empty.</param>
        /// <returns>Parse result. Never null.</returns>
        public static SshParseResult Parse(string? command)
        {
            SshCommandLexer lexer = new SshCommandLexer(command);
            return lexer.Run();
        }
    }
}
