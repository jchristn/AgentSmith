namespace Hugo.Connectors.Redis.Classification
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Splits a redis-cli style command line into arguments. Whitespace separates arguments; double quotes support the
    /// escapes <c>\"</c>, <c>\\</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>, and <c>\xHH</c>; single quotes are literal except
    /// <c>\'</c>. Thread-safe.
    /// </summary>
    public static class RedisCommandTokenizer
    {
        #region Public-Methods

        /// <summary>
        /// Tokenize a command line.
        /// </summary>
        /// <param name="line">Command line.</param>
        /// <returns>Arguments; empty for a blank line.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for an unterminated quote or a bad escape.</exception>
        public static List<string> Tokenize(string? line)
        {
            List<string> tokens = new List<string>();
            string text = line ?? String.Empty;
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && Char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;

                StringBuilder sb = new StringBuilder();
                bool quotedAny = false;
                while (i < text.Length && !Char.IsWhiteSpace(text[i]))
                {
                    char c = text[i];
                    if (c == '"')
                    {
                        quotedAny = true;
                        i++;
                        bool closed = false;
                        while (i < text.Length)
                        {
                            char q = text[i];
                            if (q == '\\' && i + 1 < text.Length)
                            {
                                char n = text[i + 1];
                                if (n == 'x' && i + 3 < text.Length && IsHex(text[i + 2]) && IsHex(text[i + 3]))
                                {
                                    sb.Append((char)Convert.ToInt32(text.Substring(i + 2, 2), 16));
                                    i += 4;
                                    continue;
                                }
                                sb.Append(n switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'a' => '\a', 'b' => '\b', _ => n });
                                i += 2;
                                continue;
                            }
                            if (q == '"')
                            {
                                closed = true;
                                i++;
                                break;
                            }
                            sb.Append(q);
                            i++;
                        }
                        if (!closed) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The command has an unterminated double quote.");
                    }
                    else if (c == '\'')
                    {
                        quotedAny = true;
                        i++;
                        bool closed = false;
                        while (i < text.Length)
                        {
                            char q = text[i];
                            if (q == '\\' && i + 1 < text.Length && text[i + 1] == '\'')
                            {
                                sb.Append('\'');
                                i += 2;
                                continue;
                            }
                            if (q == '\'')
                            {
                                closed = true;
                                i++;
                                break;
                            }
                            sb.Append(q);
                            i++;
                        }
                        if (!closed) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The command has an unterminated single quote.");
                    }
                    else
                    {
                        sb.Append(c);
                        i++;
                    }
                }

                if (sb.Length > 0 || quotedAny) tokens.Add(sb.ToString());
            }
            return tokens;
        }

        #endregion

        #region Private-Methods

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        #endregion
    }
}
