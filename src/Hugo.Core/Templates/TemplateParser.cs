namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Parses the Hugo template dialect: {{path | filter: arg}}, {{#if path}}...{{else}}...{{/if}},
    /// {{#unless path}}...{{/unless}}, {{#each path}}...{{/each}}, {{> name}}, {{! comment}}, and
    /// {{{{raw}}}}...{{{{/raw}}}} literal blocks. Block tags alone on a line remove that line from the output.
    /// Thread-safe.
    /// </summary>
    public static class TemplateParser
    {
        #region Private-Members

        private static readonly HashSet<string> _Filters = new HashSet<string>(StringComparer.Ordinal)
        {
            "default", "upper", "lower", "trim", "json", "toml", "indent", "truncate", "join"
        };

        private static readonly string _RawOpen = "{{{{raw}}}}";
        private static readonly string _RawClose = "{{{{/raw}}}}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Supported filter names.
        /// </summary>
        public static IReadOnlyCollection<string> Filters
        {
            get
            {
                return _Filters;
            }
        }

        /// <summary>
        /// Parse a template.
        /// </summary>
        /// <param name="template">Template text.</param>
        /// <param name="name">Template name for error messages.</param>
        /// <returns>Root nodes.</returns>
        /// <exception cref="TemplateException">Thrown on syntax errors.</exception>
        public static List<TemplateNode> Parse(string? template, string? name = null)
        {
            List<Token> tokens = Tokenize(template ?? String.Empty, name);
            StripStandalone(tokens);

            List<TemplateNode> root = new List<TemplateNode>();
            Stack<Frame> stack = new Stack<Frame>();
            Frame top = new Frame();
            top.Target = root;

            foreach (Token token in tokens)
            {
                if (token.IsText)
                {
                    if (token.Text.Length == 0) continue;
                    TextNode text = new TextNode();
                    text.Text = token.Text;
                    text.Line = token.Line;
                    text.Column = token.Column;
                    top.Target.Add(text);
                    continue;
                }

                string tag = token.Text.Trim();
                if (tag.StartsWith("!", StringComparison.Ordinal)) continue;

                if (tag.StartsWith("#if ", StringComparison.Ordinal) || tag.StartsWith("#unless ", StringComparison.Ordinal))
                {
                    bool negate = tag.StartsWith("#unless", StringComparison.Ordinal);
                    IfNode node = new IfNode();
                    node.Path = RequirePath(tag.Substring(negate ? 8 : 4).Trim(), token, name);
                    node.Negate = negate;
                    node.Line = token.Line;
                    node.Column = token.Column;
                    top.Target.Add(node);
                    stack.Push(top);
                    top = new Frame();
                    top.Kind = negate ? "unless" : "if";
                    top.If = node;
                    top.Target = node.Then;
                    top.Token = token;
                    continue;
                }

                if (tag == "else")
                {
                    if (top.If == null || top.InElse) throw Error("{{else}} without an open {{#if}} or {{#unless}}.", token, name);
                    top.InElse = true;
                    top.Target = top.If.Else;
                    continue;
                }

                if (tag.StartsWith("#each ", StringComparison.Ordinal))
                {
                    EachNode node = new EachNode();
                    node.Path = RequirePath(tag.Substring(6).Trim(), token, name);
                    node.Line = token.Line;
                    node.Column = token.Column;
                    top.Target.Add(node);
                    stack.Push(top);
                    top = new Frame();
                    top.Kind = "each";
                    top.Target = node.Body;
                    top.Token = token;
                    continue;
                }

                if (tag == "/if" || tag == "/unless" || tag == "/each")
                {
                    string kind = tag.Substring(1);
                    if (stack.Count == 0 || top.Kind != kind) throw Error("Unexpected {{" + tag + "}}" + (stack.Count == 0 ? "." : "; expected {{/" + top.Kind + "}}."), token, name);
                    top = stack.Pop();
                    continue;
                }

                if (tag.StartsWith(">", StringComparison.Ordinal))
                {
                    string includeName = tag.Substring(1).Trim();
                    if (includeName.Length == 0) throw Error("Include requires a template key: write {{> key}}, for example {{> persona.agent-smith}}.", token, name);
                    if (includeName.Any(Char.IsWhiteSpace))
                    {
                        string[] words = includeName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        throw Error("Invalid include {{> " + includeName + "}}: the syntax is {{> key}} with one template key and no 'include' keyword, for example {{> " + words[words.Length - 1] + "}}.", token, name);
                    }
                    IncludeNode include = new IncludeNode();
                    include.Name = includeName;
                    include.Line = token.Line;
                    include.Column = token.Column;
                    top.Target.Add(include);
                    continue;
                }

                if (tag.StartsWith("#", StringComparison.Ordinal) || tag.StartsWith("/", StringComparison.Ordinal))
                {
                    throw Error("Unknown block tag {{" + tag + "}}.", token, name);
                }

                top.Target.Add(ParseVariable(tag, token, name));
            }

            if (stack.Count > 0) throw Error("Unclosed {{#" + top.Kind + "}} block.", top.Token!, name);
            return root;
        }

        #endregion

        #region Private-Methods

        private static VariableNode ParseVariable(string expression, Token token, string? name)
        {
            List<string> parts = SplitPipes(expression);
            VariableNode node = new VariableNode();
            node.Line = token.Line;
            node.Column = token.Column;
            node.Path = RequirePath(parts[0].Trim(), token, name);

            for (int i = 1; i < parts.Count; i++)
            {
                string part = parts[i].Trim();
                TemplateFilter filter = new TemplateFilter();
                int colon = part.IndexOf(':');
                if (colon < 0)
                {
                    filter.Name = part.ToLowerInvariant();
                }
                else
                {
                    filter.Name = part.Substring(0, colon).Trim().ToLowerInvariant();
                    string argument = part.Substring(colon + 1).Trim();
                    if (argument.Length >= 2 && argument[0] == '"' && argument[argument.Length - 1] == '"')
                    {
                        filter.Argument = Unescape(argument.Substring(1, argument.Length - 2));
                        filter.ArgumentIsLiteral = true;
                    }
                    else if (argument.Length > 0 && (Char.IsDigit(argument[0]) || argument[0] == '-'))
                    {
                        filter.Argument = argument;
                        filter.ArgumentIsLiteral = true;
                    }
                    else
                    {
                        filter.Argument = RequirePath(argument, token, name);
                        filter.ArgumentIsLiteral = false;
                    }
                }
                if (!_Filters.Contains(filter.Name)) throw Error("Unknown filter '" + filter.Name + "'. Supported: " + String.Join(", ", _Filters) + ".", token, name);
                node.Filters.Add(filter);
            }
            return node;
        }

        private static List<string> SplitPipes(string expression)
        {
            List<string> parts = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];
                if (c == '\\' && quoted && i + 1 < expression.Length)
                {
                    current.Append(c).Append(expression[i + 1]);
                    i++;
                    continue;
                }
                if (c == '"') quoted = !quoted;
                if (c == '|' && !quoted)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            parts.Add(current.ToString());
            return parts;
        }

        private static string Unescape(string text)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    char next = text[i + 1];
                    sb.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next);
                    i++;
                    continue;
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        private static string RequirePath(string path, Token token, string? name)
        {
            if (path.Length == 0) throw Error("Expected a variable path.", token, name);
            foreach (char c in path)
            {
                if (!(Char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '@'))
                {
                    throw Error("Invalid character '" + c + "' in variable path '" + path + "'.", token, name);
                }
            }
            if (path.StartsWith(".", StringComparison.Ordinal) || path.EndsWith(".", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            {
                throw Error("Malformed variable path '" + path + "'.", token, name);
            }
            return path;
        }

        private static List<Token> Tokenize(string template, string? name)
        {
            List<Token> tokens = new List<Token>();
            int index = 0;
            int line = 1;
            int column = 1;
            StringBuilder text = new StringBuilder();
            int textLine = 1;
            int textColumn = 1;

            while (index < template.Length)
            {
                if (String.CompareOrdinal(template, index, _RawOpen, 0, _RawOpen.Length) == 0)
                {
                    int close = template.IndexOf(_RawClose, index + _RawOpen.Length, StringComparison.Ordinal);
                    if (close < 0) throw new TemplateException("Unclosed {{{{raw}}}} block.", name, line, column);
                    string raw = template.Substring(index + _RawOpen.Length, close - index - _RawOpen.Length);
                    if (text.Length == 0)
                    {
                        textLine = line;
                        textColumn = column;
                    }
                    text.Append(raw);
                    Advance(template, index, close + _RawClose.Length, ref line, ref column);
                    index = close + _RawClose.Length;
                    continue;
                }

                if (index + 1 < template.Length && template[index] == '{' && template[index + 1] == '{')
                {
                    int close = template.IndexOf("}}", index + 2, StringComparison.Ordinal);
                    if (close < 0) throw new TemplateException("Unclosed {{ tag.", name, line, column);
                    if (text.Length > 0 || tokens.Count == 0 || !tokens[tokens.Count - 1].IsText)
                    {
                        tokens.Add(Token.TextToken(text.ToString(), textLine, textColumn));
                        text.Clear();
                    }
                    tokens.Add(Token.TagToken(template.Substring(index + 2, close - index - 2), line, column));
                    Advance(template, index, close + 2, ref line, ref column);
                    index = close + 2;
                    textLine = line;
                    textColumn = column;
                    continue;
                }

                if (text.Length == 0)
                {
                    textLine = line;
                    textColumn = column;
                }
                text.Append(template[index]);
                Advance(template, index, index + 1, ref line, ref column);
                index++;
            }

            tokens.Add(Token.TextToken(text.ToString(), textLine, textColumn));
            return tokens;
        }

        private static void Advance(string template, int from, int to, ref int line, ref int column)
        {
            for (int i = from; i < to; i++)
            {
                if (template[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }
        }

        private static void StripStandalone(List<Token> tokens)
        {
            List<string> original = new List<string>();
            foreach (Token token in tokens) original.Add(token.Text);

            List<int> standalone = new List<int>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].IsText || !IsBlockTag(tokens[i].Text)) continue;
                if (i > 0 && !tokens[i - 1].IsText) continue;
                if (i + 1 < tokens.Count && !tokens[i + 1].IsText) continue;

                string before = i > 0 ? original[i - 1] : String.Empty;
                int lastNewline = before.LastIndexOf('\n');
                string leading = lastNewline >= 0 ? before.Substring(lastNewline + 1) : before;
                bool atLineStart = lastNewline >= 0 || i <= 1;
                if (!atLineStart || leading.Trim().Length > 0) continue;

                string after = i + 1 < tokens.Count ? original[i + 1] : String.Empty;
                int firstNewline = after.IndexOf('\n');
                string trailing = firstNewline >= 0 ? after.Substring(0, firstNewline) : after;
                if (trailing.Trim().Length > 0) continue;
                if (firstNewline < 0 && i + 2 < tokens.Count) continue;

                standalone.Add(i);
            }

            foreach (int i in standalone)
            {
                if (i > 0)
                {
                    Token previous = tokens[i - 1];
                    int lastNewline = previous.Text.LastIndexOf('\n');
                    string tail = lastNewline >= 0 ? previous.Text.Substring(lastNewline + 1) : previous.Text;
                    if (tail.Trim().Length == 0) previous.Text = previous.Text.Substring(0, previous.Text.Length - tail.Length);
                }
                if (i + 1 < tokens.Count)
                {
                    Token next = tokens[i + 1];
                    int firstNewline = next.Text.IndexOf('\n');
                    if (firstNewline >= 0 && next.Text.Substring(0, firstNewline).Trim().Length == 0) next.Text = next.Text.Substring(firstNewline + 1);
                    else if (firstNewline < 0 && next.Text.Trim().Length == 0) next.Text = String.Empty;
                }
            }
        }

        private static bool IsBlockTag(string text)
        {
            string tag = text.Trim();
            return tag.StartsWith("#", StringComparison.Ordinal) || tag.StartsWith("/", StringComparison.Ordinal) || tag == "else" || tag.StartsWith("!", StringComparison.Ordinal);
        }

        private static TemplateException Error(string detail, Token token, string? name)
        {
            return new TemplateException(detail, name, token.Line, token.Column);
        }

        #endregion
    }
}
