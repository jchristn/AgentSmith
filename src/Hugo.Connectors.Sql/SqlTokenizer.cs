namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Dialect-aware SQL tokenizer. It removes comments and recognizes string literals and quoted identifiers the way
    /// each engine does, so text inside them can never be mistaken for a keyword or a statement separator. Derived from
    /// the quote and comment rules of the vendored Tablix <c>QueryValidator</c>, extended with:
    /// MySQL backslash escapes, <c>#</c> comments, the <c>-- </c> space rule, and executable <c>/*! */</c> comments;
    /// PostgreSQL dollar quoting, <c>E''</c> escape strings, and nested block comments; SQL Server and SQLite bracket
    /// identifiers; MySQL and SQLite backtick identifiers. Stateless and thread-safe.
    /// </summary>
    public static class SqlTokenizer
    {
        #region Public-Methods

        /// <summary>
        /// Tokenize SQL text.
        /// </summary>
        /// <param name="sql">SQL text. Null is treated as empty.</param>
        /// <param name="engine">Dialect.</param>
        /// <returns>Tokens without whitespace and comments.</returns>
        /// <exception cref="SqlParseException">Thrown for an unterminated string, identifier, or comment.</exception>
        public static List<SqlToken> Tokenize(string? sql, SqlEngineEnum engine)
        {
            List<SqlToken> tokens = new List<SqlToken>();
            if (String.IsNullOrEmpty(sql)) return tokens;

            int i = 0;
            int executableCommentDepth = 0;
            int length = sql.Length;
            while (i < length)
            {
                char c = sql[i];
                char next = i + 1 < length ? sql[i + 1] : '\0';

                if (Char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '-' && next == '-')
                {
                    char after = i + 2 < length ? sql[i + 2] : '\0';
                    bool isComment = engine != SqlEngineEnum.Mysql || after == '\0' || Char.IsWhiteSpace(after) || Char.IsControl(after);
                    if (isComment)
                    {
                        i = SkipToEndOfLine(sql, i);
                        continue;
                    }
                }

                if (c == '#' && engine == SqlEngineEnum.Mysql)
                {
                    i = SkipToEndOfLine(sql, i);
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    if (engine == SqlEngineEnum.Mysql && IsExecutableComment(sql, i, out int bodyStart))
                    {
                        executableCommentDepth++;
                        i = bodyStart;
                        continue;
                    }
                    i = SkipBlockComment(sql, i, engine == SqlEngineEnum.Postgresql || engine == SqlEngineEnum.SqlServer);
                    continue;
                }

                if (c == '*' && next == '/' && executableCommentDepth > 0)
                {
                    executableCommentDepth--;
                    i += 2;
                    continue;
                }

                if (c == '\'')
                {
                    bool backslash = engine == SqlEngineEnum.Mysql;
                    i = ReadQuoted(sql, i, '\'', backslash, SqlTokenKindEnum.String, tokens);
                    continue;
                }

                if (c == '"')
                {
                    if (engine == SqlEngineEnum.Mysql) i = ReadQuoted(sql, i, '"', true, SqlTokenKindEnum.String, tokens);
                    else i = ReadQuoted(sql, i, '"', false, SqlTokenKindEnum.QuotedIdentifier, tokens);
                    continue;
                }

                if (c == '`' && (engine == SqlEngineEnum.Mysql || engine == SqlEngineEnum.Sqlite))
                {
                    i = ReadQuoted(sql, i, '`', false, SqlTokenKindEnum.QuotedIdentifier, tokens);
                    continue;
                }

                if (c == '[' && (engine == SqlEngineEnum.SqlServer || engine == SqlEngineEnum.Sqlite))
                {
                    i = ReadBracketIdentifier(sql, i, tokens);
                    continue;
                }

                if (c == '$' && engine == SqlEngineEnum.Postgresql)
                {
                    if (Char.IsDigit(next))
                    {
                        int start = i;
                        i++;
                        while (i < length && Char.IsDigit(sql[i])) i++;
                        tokens.Add(new SqlToken(SqlTokenKindEnum.Variable, sql.Substring(start, i - start), null, start));
                        continue;
                    }
                    if (TryReadDollarQuoted(sql, i, tokens, out int afterDollar))
                    {
                        i = afterDollar;
                        continue;
                    }
                }

                if (c == '@' || (c == '#' && engine == SqlEngineEnum.SqlServer) || (c == ':' && IsWordStart(next) && (i == 0 || sql[i - 1] != ':') && engine != SqlEngineEnum.SqlServer))
                {
                    int start = i;
                    i++;
                    while (i < length && (sql[i] == '@' || sql[i] == '#')) i++;
                    while (i < length && IsWordPart(sql[i])) i++;
                    if (i - start > 1 || c == '@')
                    {
                        tokens.Add(new SqlToken(SqlTokenKindEnum.Variable, sql.Substring(start, i - start), null, start));
                        continue;
                    }
                    i = start;
                }

                if (IsWordStart(c))
                {
                    int start = i;
                    while (i < length && IsWordPart(sql[i])) i++;
                    string word = sql.Substring(start, i - start);

                    if (engine == SqlEngineEnum.Postgresql && i < length && sql[i] == '\'' && String.Equals(word, "E", StringComparison.OrdinalIgnoreCase))
                    {
                        i = ReadQuoted(sql, i, '\'', true, SqlTokenKindEnum.String, tokens);
                        continue;
                    }

                    tokens.Add(new SqlToken(SqlTokenKindEnum.Word, word.ToUpperInvariant(), word, start));
                    continue;
                }

                if (Char.IsDigit(c) || (c == '.' && Char.IsDigit(next)))
                {
                    int start = i;
                    while (i < length && (Char.IsLetterOrDigit(sql[i]) || sql[i] == '.' || sql[i] == '_'
                        || ((sql[i] == '+' || sql[i] == '-') && (sql[i - 1] == 'e' || sql[i - 1] == 'E'))))
                    {
                        i++;
                    }
                    tokens.Add(new SqlToken(SqlTokenKindEnum.Number, sql.Substring(start, i - start), null, start));
                    continue;
                }

                tokens.Add(new SqlToken(SqlTokenKindEnum.Symbol, c.ToString(), null, i));
                i++;
            }

            if (executableCommentDepth > 0) throw new SqlParseException("Unterminated executable comment.", length);
            return tokens;
        }

        #endregion

        #region Private-Methods

        private static bool IsWordStart(char c)
        {
            return Char.IsLetter(c) || c == '_';
        }

        private static bool IsWordPart(char c)
        {
            return Char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }

        private static int SkipToEndOfLine(string sql, int index)
        {
            int newline = sql.IndexOf('\n', index);
            return newline < 0 ? sql.Length : newline + 1;
        }

        private static bool IsExecutableComment(string sql, int index, out int bodyStart)
        {
            bodyStart = index;
            int i = index + 2;
            if (i < sql.Length && sql[i] == 'M' && i + 1 < sql.Length && sql[i + 1] == '!') i++;
            if (i >= sql.Length || sql[i] != '!') return false;
            i++;
            while (i < sql.Length && Char.IsDigit(sql[i])) i++;
            bodyStart = i;
            return true;
        }

        private static int SkipBlockComment(string sql, int index, bool nested)
        {
            int depth = 0;
            int i = index;
            while (i < sql.Length)
            {
                if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    if (depth == 0 || nested) depth++;
                    i += 2;
                    continue;
                }
                if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                {
                    depth--;
                    i += 2;
                    if (depth == 0) return i;
                    continue;
                }
                i++;
            }
            throw new SqlParseException("Unterminated block comment.", index);
        }

        private static int ReadQuoted(string sql, int index, char quote, bool backslashEscapes, SqlTokenKindEnum kind, List<SqlToken> tokens)
        {
            StringBuilder content = new StringBuilder();
            int i = index + 1;
            while (i < sql.Length)
            {
                char c = sql[i];
                if (backslashEscapes && c == '\\' && i + 1 < sql.Length)
                {
                    content.Append(sql[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote)
                    {
                        content.Append(quote);
                        i += 2;
                        continue;
                    }
                    tokens.Add(new SqlToken(kind, sql.Substring(index, i - index + 1), content.ToString(), index));
                    return i + 1;
                }
                content.Append(c);
                i++;
            }
            throw new SqlParseException(kind == SqlTokenKindEnum.String ? "Unterminated string literal." : "Unterminated quoted identifier.", index);
        }

        private static int ReadBracketIdentifier(string sql, int index, List<SqlToken> tokens)
        {
            StringBuilder content = new StringBuilder();
            int i = index + 1;
            while (i < sql.Length)
            {
                if (sql[i] == ']')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        content.Append(']');
                        i += 2;
                        continue;
                    }
                    tokens.Add(new SqlToken(SqlTokenKindEnum.QuotedIdentifier, sql.Substring(index, i - index + 1), content.ToString(), index));
                    return i + 1;
                }
                content.Append(sql[i]);
                i++;
            }
            throw new SqlParseException("Unterminated bracket identifier.", index);
        }

        private static bool TryReadDollarQuoted(string sql, int index, List<SqlToken> tokens, out int after)
        {
            after = index;
            int i = index + 1;
            if (i < sql.Length && sql[i] != '$')
            {
                if (!IsWordStart(sql[i])) return false;
                while (i < sql.Length && (Char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                if (i >= sql.Length || sql[i] != '$') return false;
            }
            if (i >= sql.Length) return false;
            string tag = sql.Substring(index, i - index + 1);
            int bodyStart = i + 1;
            int close = sql.IndexOf(tag, bodyStart, StringComparison.Ordinal);
            if (close < 0) throw new SqlParseException("Unterminated dollar-quoted string.", index);
            tokens.Add(new SqlToken(SqlTokenKindEnum.String, sql.Substring(index, close + tag.Length - index), sql.Substring(bodyStart, close - bodyStart), index));
            after = close + tag.Length;
            return true;
        }

        #endregion
    }
}
