namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classifies SQL text as Read, Write, or Destructive (plan section 8.3). Derived from the vendored Tablix
    /// <c>QueryValidator</c> keyword-first approach and extended with a dialect-aware tokenizer, multi-statement
    /// handling, CTE bodies, <c>EXPLAIN ANALYZE</c>, implicit T-SQL statement boundaries, dynamic SQL in literals, and
    /// known side-effecting functions. Fails closed: anything it cannot parse or recognize is Destructive.
    /// Pure and thread-safe.
    /// </summary>
    public static class StatementClassifier
    {
        #region Private-Members

        private const int _MaxDepth = 16;

        private static readonly HashSet<string> _ReadStatements = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT", "VALUES", "TABLE", "SHOW", "HELP"
        };

        private static readonly HashSet<string> _WriteStatements = new HashSet<string>(StringComparer.Ordinal)
        {
            "INSERT", "UPDATE", "MERGE", "CREATE", "GRANT", "CALL", "EXEC", "EXECUTE", "REPLACE", "UPSERT", "SET",
            "VACUUM", "ANALYZE", "ANALYSE", "COPY", "LOAD", "LOCK", "UNLOCK", "REINDEX", "CLUSTER", "REFRESH", "COMMENT",
            "ATTACH", "DETACH", "DECLARE", "FETCH", "CLOSE", "OPEN", "MOVE", "LISTEN", "UNLISTEN", "NOTIFY", "CHECKPOINT",
            "OPTIMIZE", "CHECK", "REPAIR", "FLUSH", "RESET", "DISCARD", "PREPARE", "DEALLOCATE", "USE", "BEGIN", "START",
            "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE", "END", "ABORT", "BACKUP", "DBCC", "PRINT", "ENABLE", "DISABLE",
            "INSTALL", "HANDLER", "SECURITY", "IMPORT", "RECONFIGURE", "BULK", "THROW", "RAISERROR", "IF", "WHILE", "DO",
            "SAVE", "GOTO", "RETURN", "BREAK", "CONTINUE", "REVERT", "WAITFOR", "XA", "CACHE", "BINLOG", "CHANGE", "STOP"
        };

        private static readonly HashSet<string> _DestructiveStatements = new HashSet<string>(StringComparer.Ordinal)
        {
            "DELETE", "DROP", "TRUNCATE", "ALTER", "REVOKE", "DENY", "RENAME", "KILL", "SHUTDOWN", "RESTORE", "PURGE",
            "UNINSTALL"
        };

        private static readonly HashSet<string> _HiddenWriteWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "INSERT", "UPDATE", "MERGE", "CREATE", "GRANT", "CALL", "EXEC", "EXECUTE", "REPLACE", "UPSERT", "SET",
            "DECLARE", "BACKUP", "DBCC", "USE", "BEGIN", "COMMIT", "ROLLBACK", "IF", "WHILE", "WAITFOR", "BULK",
            "RECONFIGURE", "CHECKPOINT", "PRINT", "RAISERROR", "THROW", "ENABLE", "DISABLE", "SAVE", "TRUNCATE", "DELETE",
            "DROP", "ALTER", "REVOKE", "DENY", "KILL", "SHUTDOWN", "RESTORE"
        };

        private static readonly HashSet<string> _DestructiveWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "DELETE", "DROP", "TRUNCATE", "ALTER", "REVOKE", "DENY", "KILL", "SHUTDOWN", "RESTORE"
        };

        private static readonly HashSet<string> _BenignPredecessors = new HashSet<string>(StringComparer.Ordinal)
        {
            "ON", "BEFORE", "AFTER", "OR", "OF", "FOR", "INSTEAD", "GRANT", "KEY", "NO", ",", "."
        };

        private static readonly HashSet<string> _NestedDmlWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE", "DROP", "ALTER", "CREATE"
        };

        private static readonly HashSet<string> _InnerStatementStarts = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "VALUES", "TABLE", "REPLACE", "CREATE", "DECLARE",
            "EXECUTE", "UPSERT", "SHOW"
        };

        private static readonly HashSet<string> _WithMainStarts = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "VALUES", "TABLE"
        };

        private static readonly HashSet<string> _ExplainOptionWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "VERBOSE", "EXTENDED", "PARTITIONS", "QUERY", "PLAN", "COSTS", "BUFFERS", "SETTINGS", "WAL", "TIMING",
            "SUMMARY", "GENERIC_PLAN", "MEMORY", "SERIALIZE"
        };

        private static readonly HashSet<string> _ReadPragmasWithArguments = new HashSet<string>(StringComparer.Ordinal)
        {
            "TABLE_INFO", "TABLE_XINFO", "TABLE_LIST", "INDEX_LIST", "INDEX_INFO", "INDEX_XINFO", "FOREIGN_KEY_LIST",
            "FOREIGN_KEY_CHECK", "INTEGRITY_CHECK", "QUICK_CHECK", "COLLATION_LIST", "DATABASE_LIST", "FUNCTION_LIST",
            "MODULE_LIST", "PRAGMA_LIST", "COMPILE_OPTIONS"
        };

        private static readonly HashSet<string> _WritePragmas = new HashSet<string>(StringComparer.Ordinal)
        {
            "OPTIMIZE", "WAL_CHECKPOINT", "INCREMENTAL_VACUUM", "SHRINK_MEMORY"
        };

        private static readonly HashSet<string> _DestructiveFunctions = new HashSet<string>(StringComparer.Ordinal)
        {
            "PG_TERMINATE_BACKEND", "PG_CANCEL_BACKEND", "LO_UNLINK", "PG_DROP_REPLICATION_SLOT", "DBLINK_EXEC",
            "LOAD_EXTENSION", "XP_CMDSHELL", "PG_FILE_UNLINK", "PG_FILE_WRITE", "PG_PROMOTE"
        };

        private static readonly HashSet<string> _WriteFunctions = new HashSet<string>(StringComparer.Ordinal)
        {
            "NEXTVAL", "SETVAL", "SET_CONFIG", "PG_ADVISORY_LOCK", "PG_ADVISORY_XACT_LOCK", "PG_TRY_ADVISORY_LOCK",
            "PG_TRY_ADVISORY_XACT_LOCK", "PG_ADVISORY_LOCK_SHARED", "PG_RELOAD_CONF", "PG_ROTATE_LOGFILE", "PG_SWITCH_WAL",
            "PG_CREATE_RESTORE_POINT", "LO_IMPORT", "LO_EXPORT", "LO_CREATE", "LO_FROM_BYTEA", "LO_PUT", "PG_NOTIFY",
            "PG_CREATE_LOGICAL_REPLICATION_SLOT", "PG_CREATE_PHYSICAL_REPLICATION_SLOT", "DBLINK", "GET_LOCK",
            "RELEASE_LOCK", "RELEASE_ALL_LOCKS", "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "WRITEFILE"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Classify SQL text.
        /// </summary>
        /// <param name="sql">SQL text. Null or blank text is rejected.</param>
        /// <param name="engine">Dialect used to tokenize the text.</param>
        /// <param name="allowMultipleStatements">When false (default), text with more than one statement is rejected.
        /// When true, the result is the most severe statement.</param>
        /// <returns>Classification. Never null.</returns>
        public static StatementClassification Classify(string? sql, SqlEngineEnum engine, bool allowMultipleStatements = false)
        {
            StatementClassification result = new StatementClassification();
            if (String.IsNullOrWhiteSpace(sql))
            {
                result.Effect = EffectEnum.Destructive;
                result.Rejected = true;
                result.Reason = "empty statement";
                return result;
            }

            List<StatementVerdict> verdicts;
            try
            {
                verdicts = ClassifyText(sql, engine, 0);
            }
            catch (SqlParseException e)
            {
                result.Effect = EffectEnum.Destructive;
                result.Reason = "unparseable SQL (" + e.Message + "); treated as destructive";
                result.StatementTypes.Add("UNKNOWN");
                return result;
            }

            if (verdicts.Count == 0)
            {
                result.Effect = EffectEnum.Destructive;
                result.Rejected = true;
                result.Reason = "no statement found (only comments or separators)";
                return result;
            }

            result.StatementCount = verdicts.Count;
            result.StatementTypes = verdicts.Select(v => v.Type).ToList();
            result.Parsed = verdicts.All(v => v.Recognized);

            StatementVerdict worst = verdicts[0];
            foreach (StatementVerdict v in verdicts)
            {
                if (v.Effect > worst.Effect) worst = v;
            }
            result.Effect = worst.Effect;

            if (verdicts.Count > 1 && !allowMultipleStatements)
            {
                result.Effect = EffectEnum.Destructive;
                result.Rejected = true;
                result.Reason = "multiple statements (" + verdicts.Count + ") are not allowed on this resource; send one statement per call";
            }
            else if (verdicts.Count > 1)
            {
                result.Reason = "batch of " + verdicts.Count + " statements; most severe: " + worst.Reason;
            }
            else
            {
                result.Reason = worst.Reason;
            }
            return result;
        }

        /// <summary>
        /// Split a token list into statements on top-level <c>;</c> separators, dropping empty statements.
        /// </summary>
        /// <param name="tokens">Tokens.</param>
        /// <returns>Statements.</returns>
        public static List<List<SqlToken>> SplitStatements(List<SqlToken> tokens)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            List<List<SqlToken>> statements = new List<List<SqlToken>>();
            List<SqlToken> current = new List<SqlToken>();
            foreach (SqlToken token in tokens)
            {
                if (token.IsSymbol(';'))
                {
                    if (current.Count > 0) statements.Add(current);
                    current = new List<SqlToken>();
                    continue;
                }
                current.Add(token);
            }
            if (current.Count > 0) statements.Add(current);
            return statements;
        }

        #endregion

        #region Private-Methods

        private static List<StatementVerdict> ClassifyText(string sql, SqlEngineEnum engine, int depth)
        {
            List<SqlToken> tokens = SqlTokenizer.Tokenize(sql, engine);
            List<StatementVerdict> verdicts = new List<StatementVerdict>();
            foreach (List<SqlToken> statement in SplitStatements(tokens))
            {
                verdicts.Add(ClassifyStatement(statement, engine, depth));
            }
            return verdicts;
        }

        private static StatementVerdict ClassifyStatement(List<SqlToken> tokens, SqlEngineEnum engine, int depth)
        {
            if (depth > _MaxDepth) return StatementVerdict.Unrecognized("statement nesting too deep");

            int start = 0;
            while (start < tokens.Count && tokens[start].IsSymbol('(')) start++;
            if (start >= tokens.Count) return StatementVerdict.Unrecognized("statement has no keyword");

            SqlToken first = tokens[start];
            if (first.Kind != SqlTokenKindEnum.Word) return StatementVerdict.Unrecognized("statement does not start with a keyword");

            string keyword = first.Text;
            StatementVerdict verdict;
            bool scan = true;

            if (keyword == "EXPLAIN" || keyword == "DESCRIBE" || keyword == "DESC")
            {
                verdict = ClassifyExplain(tokens, start, engine, depth, out scan);
            }
            else if (keyword == "WITH")
            {
                verdict = ClassifyWith(tokens, start, engine, depth);
            }
            else if (keyword == "PRAGMA")
            {
                verdict = ClassifyPragma(tokens, start);
            }
            else if (keyword == "SELECT")
            {
                verdict = ClassifySelect(tokens, start);
            }
            else if (keyword == "SHOW" || keyword == "HELP")
            {
                verdict = new StatementVerdict(EffectEnum.Read, keyword, "statement type " + keyword);
                scan = false;
            }
            else if (_ReadStatements.Contains(keyword))
            {
                verdict = new StatementVerdict(EffectEnum.Read, keyword, "statement type " + keyword);
            }
            else if (keyword == "WAITFOR" && start + 1 < tokens.Count && (tokens[start + 1].IsWord("DELAY") || tokens[start + 1].IsWord("TIME")))
            {
                verdict = new StatementVerdict(EffectEnum.Read, "WAITFOR", "statement type WAITFOR DELAY");
            }
            else if (_DestructiveStatements.Contains(keyword))
            {
                verdict = new StatementVerdict(EffectEnum.Destructive, keyword, "statement type " + keyword);
            }
            else if (_WriteStatements.Contains(keyword))
            {
                verdict = ClassifyWrite(tokens, start, keyword, engine, depth);
            }
            else
            {
                return StatementVerdict.Unrecognized("unrecognized statement type " + keyword + "; treated as destructive");
            }

            if (!scan || verdict.Effect == EffectEnum.Destructive) return verdict;

            verdict = Max(verdict, ScanHiddenStatements(tokens, start, engine, depth, verdict.Effect == EffectEnum.Read));
            if (verdict.Effect == EffectEnum.Destructive) return verdict;
            verdict = Max(verdict, ScanNestedDml(tokens, start));
            if (verdict.Effect == EffectEnum.Destructive) return verdict;
            verdict = Max(verdict, ScanFunctions(tokens, start));
            return verdict;
        }

        private static StatementVerdict ClassifySelect(List<SqlToken> tokens, int start)
        {
            for (int i = start + 1; i < tokens.Count; i++)
            {
                SqlToken t = tokens[i];
                if (t.IsWord("INTO")) return new StatementVerdict(EffectEnum.Write, "SELECT INTO", "SELECT ... INTO writes a table, variable, or file");
                if (t.IsWord("FOR") && i + 1 < tokens.Count)
                {
                    SqlToken n = tokens[i + 1];
                    if (n.IsWord("UPDATE") || n.IsWord("SHARE") || n.IsWord("NO") || n.IsWord("KEY"))
                        return new StatementVerdict(EffectEnum.Write, "SELECT FOR UPDATE", "SELECT ... FOR " + n.Text + " takes row locks");
                }
                if (t.IsWord("LOCK") && i + 1 < tokens.Count && tokens[i + 1].IsWord("IN"))
                    return new StatementVerdict(EffectEnum.Write, "SELECT FOR UPDATE", "SELECT ... LOCK IN SHARE MODE takes row locks");
            }
            return new StatementVerdict(EffectEnum.Read, "SELECT", "statement type SELECT");
        }

        private static StatementVerdict ClassifyWrite(List<SqlToken> tokens, int start, string keyword, SqlEngineEnum engine, int depth)
        {
            StatementVerdict verdict = new StatementVerdict(EffectEnum.Write, keyword, "statement type " + keyword);

            if (keyword == "MERGE" && tokens.Skip(start + 1).Any(t => t.IsWord("DELETE")))
                return new StatementVerdict(EffectEnum.Destructive, "MERGE", "MERGE with a DELETE action");

            if (keyword == "EXEC" || keyword == "EXECUTE" || keyword == "DO" || keyword == "PREPARE" || tokens.Any(t => t.IsWord("SP_EXECUTESQL")))
            {
                foreach (SqlToken literal in tokens.Where(t => t.Kind == SqlTokenKindEnum.String))
                {
                    StatementVerdict? inner = ClassifyLiteral(literal.Value, engine, depth);
                    if (inner != null && inner.Effect > verdict.Effect)
                        verdict = new StatementVerdict(inner.Effect, keyword, "dynamic SQL in " + keyword + ": " + inner.Reason);
                }
            }
            return verdict;
        }

        private static StatementVerdict? ClassifyLiteral(string content, SqlEngineEnum engine, int depth)
        {
            if (String.IsNullOrWhiteSpace(content)) return null;
            try
            {
                List<StatementVerdict> verdicts = ClassifyText(content, engine, depth + 1);
                List<StatementVerdict> recognized = verdicts.Where(v => v.Recognized).ToList();
                if (recognized.Count == 0) return null;
                return recognized.OrderByDescending(v => v.Effect).First();
            }
            catch (SqlParseException)
            {
                return null;
            }
        }

        private static StatementVerdict ClassifyExplain(List<SqlToken> tokens, int start, SqlEngineEnum engine, int depth, out bool scan)
        {
            scan = false;
            bool analyze = false;
            int i = start + 1;
            while (i < tokens.Count)
            {
                SqlToken t = tokens[i];
                if (t.IsSymbol('('))
                {
                    SqlToken inside = i + 1 < tokens.Count ? tokens[i + 1] : t;
                    if (inside.IsSymbol('(') || (inside.Kind == SqlTokenKindEnum.Word && _InnerStatementStarts.Contains(inside.Text))) break;
                    int close = MatchParen(tokens, i);
                    if (close < 0) return StatementVerdict.Unrecognized("unbalanced parentheses in EXPLAIN options");
                    for (int j = i + 1; j < close; j++)
                    {
                        if (tokens[j].IsWord("ANALYZE") || tokens[j].IsWord("ANALYSE")) analyze = true;
                    }
                    i = close + 1;
                    if (i < tokens.Count && !tokens[i].IsSymbol('(')) continue;
                    break;
                }
                if (t.IsWord("ANALYZE") || t.IsWord("ANALYSE"))
                {
                    analyze = true;
                    i++;
                    continue;
                }
                if (t.Kind == SqlTokenKindEnum.Word && _ExplainOptionWords.Contains(t.Text))
                {
                    i++;
                    continue;
                }
                if (t.IsWord("FORMAT"))
                {
                    i++;
                    if (i < tokens.Count && tokens[i].IsSymbol('=')) i++;
                    i++;
                    continue;
                }
                if (t.IsWord("FOR")) return new StatementVerdict(EffectEnum.Read, "EXPLAIN", "EXPLAIN FOR CONNECTION reads a plan");
                break;
            }

            if (i >= tokens.Count) return new StatementVerdict(EffectEnum.Read, "DESCRIBE", "statement type DESCRIBE");

            SqlToken next = tokens[i];
            bool isStatement = next.IsSymbol('(') || (next.Kind == SqlTokenKindEnum.Word && _InnerStatementStarts.Contains(next.Text));
            if (!isStatement) return new StatementVerdict(EffectEnum.Read, "DESCRIBE", "statement type DESCRIBE");

            StatementVerdict inner = ClassifyStatement(tokens.GetRange(i, tokens.Count - i), engine, depth + 1);
            if (!inner.Recognized) return inner;
            if (analyze)
            {
                StatementVerdict v = new StatementVerdict(inner.Effect, "EXPLAIN ANALYZE " + inner.Type, "EXPLAIN ANALYZE executes the statement (" + inner.Reason + ")");
                return v;
            }
            return new StatementVerdict(EffectEnum.Read, "EXPLAIN", "EXPLAIN without ANALYZE does not execute the statement");
        }

        private static StatementVerdict ClassifyWith(List<SqlToken> tokens, int start, SqlEngineEnum engine, int depth)
        {
            int i = start + 1;
            if (i < tokens.Count && tokens[i].IsWord("RECURSIVE")) i++;

            StatementVerdict worst = new StatementVerdict(EffectEnum.Read, "SELECT", "statement type SELECT");
            bool sawCte = false;
            while (true)
            {
                if (i >= tokens.Count) return StatementVerdict.Unrecognized("incomplete WITH clause");
                SqlToken name = tokens[i];
                if (name.Kind != SqlTokenKindEnum.Word && name.Kind != SqlTokenKindEnum.QuotedIdentifier) return StatementVerdict.Unrecognized("unparseable WITH clause");
                i++;
                if (i < tokens.Count && tokens[i].IsSymbol('('))
                {
                    int close = MatchParen(tokens, i);
                    if (close < 0) return StatementVerdict.Unrecognized("unbalanced parentheses in WITH clause");
                    i = close + 1;
                }
                if (i >= tokens.Count || !tokens[i].IsWord("AS")) return StatementVerdict.Unrecognized("unparseable WITH clause (expected AS)");
                i++;
                if (i < tokens.Count && tokens[i].IsWord("NOT")) i++;
                if (i < tokens.Count && tokens[i].IsWord("MATERIALIZED")) i++;
                if (i >= tokens.Count || !tokens[i].IsSymbol('(')) return StatementVerdict.Unrecognized("unparseable WITH clause (expected a parenthesized query)");
                int bodyClose = MatchParen(tokens, i);
                if (bodyClose < 0) return StatementVerdict.Unrecognized("unbalanced parentheses in WITH clause");

                List<SqlToken> body = tokens.GetRange(i + 1, bodyClose - i - 1);
                StatementVerdict bodyVerdict = ClassifyStatement(body, engine, depth + 1);
                if (!bodyVerdict.Recognized) return bodyVerdict;
                if (bodyVerdict.Effect > worst.Effect)
                    worst = new StatementVerdict(bodyVerdict.Effect, bodyVerdict.Type, "CTE contains " + bodyVerdict.Reason);
                sawCte = true;
                i = bodyClose + 1;

                if (i < tokens.Count && (tokens[i].IsWord("SEARCH") || tokens[i].IsWord("CYCLE")))
                {
                    int level = 0;
                    while (i < tokens.Count)
                    {
                        if (tokens[i].IsSymbol('(')) level++;
                        else if (tokens[i].IsSymbol(')')) level--;
                        else if (level == 0 && (tokens[i].IsSymbol(',') || (tokens[i].Kind == SqlTokenKindEnum.Word && _WithMainStarts.Contains(tokens[i].Text)))) break;
                        i++;
                    }
                }

                if (i < tokens.Count && tokens[i].IsSymbol(','))
                {
                    i++;
                    continue;
                }
                break;
            }

            if (!sawCte || i >= tokens.Count) return StatementVerdict.Unrecognized("WITH clause without a main statement");
            StatementVerdict main = ClassifyStatement(tokens.GetRange(i, tokens.Count - i), engine, depth + 1);
            if (!main.Recognized) return main;
            if (worst.Effect > main.Effect) return worst;
            return new StatementVerdict(main.Effect, main.Type, "WITH ... " + main.Reason);
        }

        private static StatementVerdict ClassifyPragma(List<SqlToken> tokens, int start)
        {
            int i = start + 1;
            if (i >= tokens.Count || tokens[i].Kind != SqlTokenKindEnum.Word) return StatementVerdict.Unrecognized("PRAGMA without a name");
            string name = tokens[i].Text;
            i++;
            if (i + 1 < tokens.Count && tokens[i].IsSymbol('.') && tokens[i + 1].Kind == SqlTokenKindEnum.Word)
            {
                name = tokens[i + 1].Text;
                i += 2;
            }

            if (tokens.Skip(i).Any(t => t.IsSymbol('='))) return new StatementVerdict(EffectEnum.Write, "PRAGMA", "PRAGMA " + name.ToLowerInvariant() + " assignment");
            if (_WritePragmas.Contains(name)) return new StatementVerdict(EffectEnum.Write, "PRAGMA", "PRAGMA " + name.ToLowerInvariant() + " modifies the database");
            bool hasArgument = i < tokens.Count && tokens[i].IsSymbol('(');
            if (hasArgument && !_ReadPragmasWithArguments.Contains(name)) return new StatementVerdict(EffectEnum.Write, "PRAGMA", "PRAGMA " + name.ToLowerInvariant() + "(value) sets a value");
            return new StatementVerdict(EffectEnum.Read, "PRAGMA", "PRAGMA " + name.ToLowerInvariant() + " reads a value");
        }

        private static StatementVerdict? ScanHiddenStatements(List<SqlToken> tokens, int start, SqlEngineEnum engine, int depth, bool includeWrites)
        {
            int level = 0;
            for (int i = start + 1; i < tokens.Count; i++)
            {
                SqlToken t = tokens[i];
                if (t.IsSymbol('('))
                {
                    level++;
                    continue;
                }
                if (t.IsSymbol(')'))
                {
                    level--;
                    continue;
                }
                if (level > 0 || t.Kind != SqlTokenKindEnum.Word) continue;

                bool destructive = _DestructiveWords.Contains(t.Text);
                bool write = includeWrites && _HiddenWriteWords.Contains(t.Text);
                if (!destructive && !write) continue;

                SqlToken? previous = tokens[i - 1];
                string previousText = previous.Kind == SqlTokenKindEnum.Word || previous.Kind == SqlTokenKindEnum.Symbol ? previous.Text : String.Empty;
                if (_BenignPredecessors.Contains(previousText)) continue;
                bool callsFunction = i + 1 < tokens.Count && tokens[i + 1].IsSymbol('(');
                if (callsFunction && t.Text != "EXEC" && t.Text != "EXECUTE") continue;
                if (t.Text == "SET" && IsCharacterSet(tokens, i)) continue;
                if (t.Text == "USE" && i + 1 < tokens.Count && (tokens[i + 1].IsWord("INDEX") || tokens[i + 1].IsWord("KEY"))) continue;

                StatementVerdict hidden = ClassifyStatement(tokens.GetRange(i, tokens.Count - i), engine, depth + 1);
                if (hidden.Effect == EffectEnum.Read) continue;
                return new StatementVerdict(hidden.Effect, hidden.Type, "contains a second statement without a separator (" + hidden.Reason + ")");
            }
            return null;
        }

        private static bool IsCharacterSet(List<SqlToken> tokens, int index)
        {
            if (index == 0) return false;
            SqlToken previous = tokens[index - 1];
            return previous.IsWord("CHARACTER") || previous.IsWord("CHARSET");
        }

        private static StatementVerdict? ScanNestedDml(List<SqlToken> tokens, int start)
        {
            for (int i = start; i + 1 < tokens.Count; i++)
            {
                if (!tokens[i].IsSymbol('(')) continue;
                SqlToken next = tokens[i + 1];
                if (next.Kind != SqlTokenKindEnum.Word || !_NestedDmlWords.Contains(next.Text)) continue;
                if (i + 2 < tokens.Count && tokens[i + 2].IsSymbol('(')) continue;
                EffectEnum effect = _DestructiveStatements.Contains(next.Text) ? EffectEnum.Destructive : EffectEnum.Write;
                return new StatementVerdict(effect, next.Text, "nested data-modifying statement " + next.Text);
            }
            return null;
        }

        private static StatementVerdict? ScanFunctions(List<SqlToken> tokens, int start)
        {
            StatementVerdict? found = null;
            for (int i = start; i + 1 < tokens.Count; i++)
            {
                SqlToken t = tokens[i];
                if (t.Kind != SqlTokenKindEnum.Word) continue;
                bool isCall = tokens[i + 1].IsSymbol('(') || t.Text == "XP_CMDSHELL";
                if (!isCall) continue;
                if (_DestructiveFunctions.Contains(t.Text))
                    return new StatementVerdict(EffectEnum.Destructive, "CALL " + t.Text, "calls " + t.Text.ToLowerInvariant() + "()");
                if (found == null && _WriteFunctions.Contains(t.Text))
                    found = new StatementVerdict(EffectEnum.Write, "CALL " + t.Text, "calls side-effecting function " + t.Text.ToLowerInvariant() + "()");
            }
            if (tokens.Count > 0 && tokens[tokens.Count - 1].IsWord("XP_CMDSHELL"))
                return new StatementVerdict(EffectEnum.Destructive, "CALL XP_CMDSHELL", "calls xp_cmdshell()");
            return found;
        }

        private static StatementVerdict Max(StatementVerdict current, StatementVerdict? candidate)
        {
            if (candidate == null) return current;
            return candidate.Effect > current.Effect ? candidate : current;
        }

        private static int MatchParen(List<SqlToken> tokens, int open)
        {
            int level = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i].IsSymbol('(')) level++;
                else if (tokens[i].IsSymbol(')'))
                {
                    level--;
                    if (level == 0) return i;
                }
            }
            return -1;
        }

        #endregion
    }
}
