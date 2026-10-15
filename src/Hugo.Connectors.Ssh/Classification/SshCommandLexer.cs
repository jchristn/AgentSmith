namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Single-use lexer that splits a POSIX shell command line into simple command segments. It understands quoting,
    /// escapes, command and process substitution, pipelines, lists, subshell groups, and redirections. Constructs it
    /// cannot analyze reliably (here-documents, function definitions, unbalanced quotes) produce a parse failure so the
    /// caller fails closed. Not thread-safe; create one instance per parse.
    /// </summary>
    internal class SshCommandLexer
    {
        #region Private-Members

        private readonly string _Input;
        private int _Pos = 0;
        private readonly StringBuilder _Word = new StringBuilder();
        private bool _InWord = false;
        private bool _WordQuoted = false;
        private List<string> _Words = new List<string>();
        private List<SshRedirection> _Redirections = new List<SshRedirection>();
        private string? _PendingOperator = null;
        private int? _PendingDescriptor = null;
        private readonly List<SshCommandSegment> _Segments = new List<SshCommandSegment>();
        private readonly List<string> _Substitutions = new List<string>();
        private string? _Error = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="input">Command line. Null is treated as empty.</param>
        public SshCommandLexer(string? input)
        {
            _Input = input ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the lexer once.
        /// </summary>
        /// <returns>Parse result.</returns>
        public SshParseResult Run()
        {
            if (_Input.IndexOf('\0') >= 0) return SshParseResult.Fail("command contains a NUL character");

            while (_Pos < _Input.Length && _Error == null)
            {
                char c = _Input[_Pos];
                switch (c)
                {
                    case ' ':
                    case '\t':
                    case '\r':
                        EndWord();
                        _Pos++;
                        break;
                    case '\n':
                        EndSegment();
                        _Pos++;
                        break;
                    case '#':
                        if (_InWord)
                        {
                            Append(c);
                            _Pos++;
                        }
                        else
                        {
                            int newline = _Input.IndexOf('\n', _Pos);
                            _Pos = newline < 0 ? _Input.Length : newline;
                        }
                        break;
                    case '\'':
                        ReadSingleQuoted();
                        break;
                    case '"':
                        ReadDoubleQuoted();
                        break;
                    case '\\':
                        ReadEscape();
                        break;
                    case '$':
                        ReadDollar();
                        break;
                    case '`':
                        ReadBacktick();
                        break;
                    case '|':
                        EndWord();
                        _Pos += (Peek(1) == '|' || Peek(1) == '&') ? 2 : 1;
                        EndSegment();
                        break;
                    case '&':
                        if (Peek(1) == '&')
                        {
                            _Pos += 2;
                            EndSegment();
                        }
                        else if (Peek(1) == '>')
                        {
                            EndWord();
                            if (Peek(2) == '>')
                            {
                                _Pos += 3;
                                BeginRedirection("&>>", null);
                            }
                            else
                            {
                                _Pos += 2;
                                BeginRedirection("&>", null);
                            }
                        }
                        else
                        {
                            _Pos++;
                            EndSegment();
                        }
                        break;
                    case ';':
                        if (Peek(1) == ';' || Peek(1) == '&')
                        {
                            Fail("case statements are not supported");
                            break;
                        }
                        _Pos++;
                        EndSegment();
                        break;
                    case '(':
                        if (_InWord)
                        {
                            Fail("function definitions are not supported");
                            break;
                        }
                        if (NextNonBlank(_Pos + 1) == ')')
                        {
                            Fail("function definitions are not supported");
                            break;
                        }
                        _Pos++;
                        EndSegment();
                        break;
                    case ')':
                        _Pos++;
                        EndSegment();
                        break;
                    case '<':
                    case '>':
                        ReadRedirection();
                        break;
                    default:
                        Append(c);
                        _Pos++;
                        break;
                }
            }

            if (_Error == null) EndSegment();
            if (_Error != null) return SshParseResult.Fail(_Error);

            SshParseResult result = new SshParseResult();
            result.Segments = _Segments;
            result.Substitutions = _Substitutions;
            return result;
        }

        #endregion

        #region Private-Methods

        private char Peek(int offset)
        {
            int i = _Pos + offset;
            return i >= 0 && i < _Input.Length ? _Input[i] : '\0';
        }

        private char NextNonBlank(int start)
        {
            for (int i = start; i < _Input.Length; i++)
            {
                if (_Input[i] == ' ' || _Input[i] == '\t') continue;
                return _Input[i];
            }
            return '\0';
        }

        private void Fail(string message)
        {
            if (_Error == null) _Error = message;
        }

        private void Append(char c)
        {
            _Word.Append(c);
            _InWord = true;
        }

        private void Append(string s)
        {
            _Word.Append(s);
            _InWord = true;
        }

        private void EndWord()
        {
            if (!_InWord) return;
            string word = _Word.ToString();
            _Word.Clear();
            _InWord = false;
            _WordQuoted = false;

            if (_PendingOperator != null)
            {
                _Redirections.Add(new SshRedirection { Operator = _PendingOperator, FileDescriptor = _PendingDescriptor, Target = word });
                _PendingOperator = null;
                _PendingDescriptor = null;
                return;
            }
            _Words.Add(word);
        }

        private void EndSegment()
        {
            EndWord();
            if (_PendingOperator != null)
            {
                Fail("redirection '" + _PendingOperator + "' has no target");
                return;
            }
            if (_Words.Count > 0 || _Redirections.Count > 0)
            {
                _Segments.Add(new SshCommandSegment { Words = _Words, Redirections = _Redirections });
                _Words = new List<string>();
                _Redirections = new List<SshRedirection>();
            }
        }

        private void BeginRedirection(string op, int? descriptor)
        {
            if (_PendingOperator != null)
            {
                Fail("redirection '" + _PendingOperator + "' has no target");
                return;
            }
            _PendingOperator = op;
            _PendingDescriptor = descriptor;
        }

        private void ReadSingleQuoted()
        {
            int end = _Input.IndexOf('\'', _Pos + 1);
            if (end < 0)
            {
                Fail("unterminated single quote");
                return;
            }
            Append(_Input.Substring(_Pos + 1, end - _Pos - 1));
            _WordQuoted = true;
            _Pos = end + 1;
        }

        private void ReadDoubleQuoted()
        {
            _Pos++;
            _InWord = true;
            _WordQuoted = true;
            while (_Pos < _Input.Length && _Error == null)
            {
                char c = _Input[_Pos];
                if (c == '"')
                {
                    _Pos++;
                    return;
                }
                if (c == '\\')
                {
                    char next = Peek(1);
                    if (next == '\0')
                    {
                        Fail("unterminated double quote");
                        return;
                    }
                    if (next == '\n')
                    {
                        _Pos += 2;
                        continue;
                    }
                    if (next == '"' || next == '\\' || next == '$' || next == '`') Append(next);
                    else
                    {
                        Append('\\');
                        Append(next);
                    }
                    _Pos += 2;
                    continue;
                }
                if (c == '$')
                {
                    ReadDollar();
                    continue;
                }
                if (c == '`')
                {
                    ReadBacktick();
                    continue;
                }
                Append(c);
                _Pos++;
            }
            Fail("unterminated double quote");
        }

        private void ReadEscape()
        {
            char next = Peek(1);
            if (next == '\0')
            {
                Fail("command ends with a backslash");
                return;
            }
            if (next == '\n')
            {
                _Pos += 2;
                return;
            }
            Append(next);
            _WordQuoted = true;
            _Pos += 2;
        }

        private void ReadDollar()
        {
            char next = Peek(1);
            if (next == '(' && Peek(2) == '(')
            {
                int close = FindClosingParen(_Pos + 2);
                if (close < 0)
                {
                    Fail("unterminated arithmetic expansion");
                    return;
                }
                Append(_Input.Substring(_Pos, close - _Pos + 1));
                _Pos = close + 1;
                return;
            }
            if (next == '(')
            {
                int close = FindClosingParen(_Pos + 2);
                if (close < 0)
                {
                    Fail("unterminated command substitution");
                    return;
                }
                string inner = _Input.Substring(_Pos + 2, close - _Pos - 2);
                _Substitutions.Add(inner);
                Append("$(" + inner + ")");
                _Pos = close + 1;
                return;
            }
            if (next == '{')
            {
                int depth = 0;
                for (int i = _Pos + 1; i < _Input.Length; i++)
                {
                    if (_Input[i] == '{') depth++;
                    else if (_Input[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            string expansion = _Input.Substring(_Pos, i - _Pos + 1);
                            if (expansion.Contains("$(", StringComparison.Ordinal) || expansion.Contains('`'))
                            {
                                Fail("command substitution inside parameter expansion is not supported");
                                return;
                            }
                            Append(expansion);
                            _Pos = i + 1;
                            return;
                        }
                    }
                }
                Fail("unterminated parameter expansion");
                return;
            }
            if (next == '\'')
            {
                StringBuilder sb = new StringBuilder();
                int i = _Pos + 2;
                while (i < _Input.Length)
                {
                    char c = _Input[i];
                    if (c == '\\' && i + 1 < _Input.Length)
                    {
                        sb.Append(c).Append(_Input[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (c == '\'')
                    {
                        Append(sb.ToString());
                        _WordQuoted = true;
                        _Pos = i + 1;
                        return;
                    }
                    sb.Append(c);
                    i++;
                }
                Fail("unterminated ANSI-C quote");
                return;
            }
            Append('$');
            _Pos++;
        }

        private void ReadBacktick()
        {
            StringBuilder inner = new StringBuilder();
            int i = _Pos + 1;
            while (i < _Input.Length)
            {
                char c = _Input[i];
                if (c == '\\' && i + 1 < _Input.Length)
                {
                    char n = _Input[i + 1];
                    if (n == '`' || n == '\\' || n == '$') inner.Append(n);
                    else inner.Append(c).Append(n);
                    i += 2;
                    continue;
                }
                if (c == '`')
                {
                    _Substitutions.Add(inner.ToString());
                    Append("`" + inner.ToString() + "`");
                    _Pos = i + 1;
                    return;
                }
                inner.Append(c);
                i++;
            }
            Fail("unterminated backtick command substitution");
        }

        private int FindClosingParen(int start)
        {
            int depth = 1;
            int i = start;
            while (i < _Input.Length)
            {
                char c = _Input[i];
                if (c == '\\')
                {
                    i += 2;
                    continue;
                }
                if (c == '\'')
                {
                    int end = _Input.IndexOf('\'', i + 1);
                    if (end < 0) return -1;
                    i = end + 1;
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    while (i < _Input.Length && _Input[i] != '"')
                    {
                        if (_Input[i] == '\\') i++;
                        i++;
                    }
                    if (i >= _Input.Length) return -1;
                    i++;
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
                i++;
            }
            return -1;
        }

        private void ReadRedirection()
        {
            int? descriptor = null;
            if (_InWord)
            {
                string current = _Word.ToString();
                if (!_WordQuoted && current.Length > 0 && current.Length <= 3 && current.All(Char.IsDigit))
                {
                    descriptor = Int32.Parse(current, NumberStyles.None, CultureInfo.InvariantCulture);
                    _Word.Clear();
                    _InWord = false;
                }
                else
                {
                    EndWord();
                }
            }

            char c = _Input[_Pos];
            char n1 = Peek(1);
            char n2 = Peek(2);

            if (c == '<')
            {
                if (n1 == '<' && n2 == '<')
                {
                    _Pos += 3;
                    BeginRedirection("<<<", descriptor);
                    return;
                }
                if (n1 == '<')
                {
                    Fail("here-documents are not supported");
                    return;
                }
                if (n1 == '(')
                {
                    ReadProcessSubstitution();
                    return;
                }
                if (n1 == '&')
                {
                    _Pos += 2;
                    BeginRedirection("<&", descriptor);
                    return;
                }
                if (n1 == '>')
                {
                    _Pos += 2;
                    BeginRedirection("<>", descriptor);
                    return;
                }
                _Pos++;
                BeginRedirection("<", descriptor);
                return;
            }

            if (n1 == '>')
            {
                _Pos += 2;
                BeginRedirection(">>", descriptor);
                return;
            }
            if (n1 == '|')
            {
                _Pos += 2;
                BeginRedirection(">|", descriptor);
                return;
            }
            if (n1 == '&')
            {
                _Pos += 2;
                BeginRedirection(">&", descriptor);
                return;
            }
            if (n1 == '(')
            {
                ReadProcessSubstitution();
                return;
            }
            _Pos++;
            BeginRedirection(">", descriptor);
        }

        private void ReadProcessSubstitution()
        {
            int close = FindClosingParen(_Pos + 2);
            if (close < 0)
            {
                Fail("unterminated process substitution");
                return;
            }
            string inner = _Input.Substring(_Pos + 2, close - _Pos - 2);
            _Substitutions.Add(inner);
            Append(_Input.Substring(_Pos, close - _Pos + 1));
            _Pos = close + 1;
        }

        #endregion
    }
}
