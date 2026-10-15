namespace Hugo.Core.Agent
{
    using System;
    using System.Text;

    /// <summary>
    /// Incremental splitter that separates reasoning in <c>&lt;think&gt;</c>...<c>&lt;/think&gt;</c> blocks from the visible
    /// answer as text streams in (from Wilson and Pneuma <c>ThinkParser</c>). A marker split across chunks is held back
    /// until it can be classified. One instance per model response; not thread-safe.
    /// </summary>
    public class ThinkBlockParser
    {
        #region Public-Members

        /// <summary>
        /// Opening marker. Default <c>&lt;think&gt;</c>.
        /// </summary>
        public string OpenMarker
        {
            get
            {
                return _Open;
            }
            set
            {
                _Open = String.IsNullOrEmpty(value) ? "<think>" : value;
            }
        }

        /// <summary>
        /// Closing marker. Default <c>&lt;/think&gt;</c>.
        /// </summary>
        public string CloseMarker
        {
            get
            {
                return _Close;
            }
            set
            {
                _Close = String.IsNullOrEmpty(value) ? "</think>" : value;
            }
        }

        /// <summary>
        /// All reasoning captured so far.
        /// </summary>
        public string Thinking
        {
            get
            {
                return _Thinking.ToString();
            }
        }

        /// <summary>
        /// All visible text emitted so far.
        /// </summary>
        public string Visible
        {
            get
            {
                return _Visible.ToString();
            }
        }

        /// <summary>
        /// Reasoning produced by the last <see cref="Feed"/> or <see cref="Finish"/> call.
        /// </summary>
        public string LastThinkingDelta { get; private set; } = String.Empty;

        #endregion

        #region Private-Members

        private string _Open = "<think>";
        private string _Close = "</think>";
        private bool _Inside = false;
        private string _Pending = String.Empty;
        private readonly StringBuilder _Thinking = new StringBuilder();
        private readonly StringBuilder _Visible = new StringBuilder();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Strip every think block from complete text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Visible text.</returns>
        public static string Strip(string? text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            ThinkBlockParser parser = new ThinkBlockParser();
            parser.Feed(text);
            parser.Finish();
            return parser.Visible;
        }

        /// <summary>
        /// Feed a fragment. Returns the visible text safe to emit now; reasoning from this call is in
        /// <see cref="LastThinkingDelta"/>.
        /// </summary>
        /// <param name="text">Fragment.</param>
        /// <returns>Visible text (may be empty).</returns>
        public string Feed(string? text)
        {
            int thinkingStart = _Thinking.Length;
            _Pending += text ?? String.Empty;
            StringBuilder visible = new StringBuilder();

            while (_Pending.Length > 0)
            {
                if (!_Inside)
                {
                    int i = _Pending.IndexOf(_Open, StringComparison.Ordinal);
                    if (i >= 0)
                    {
                        visible.Append(_Pending, 0, i);
                        _Pending = _Pending.Substring(i + _Open.Length);
                        _Inside = true;
                    }
                    else
                    {
                        int keep = PartialTailLength(_Pending, _Open);
                        visible.Append(_Pending, 0, _Pending.Length - keep);
                        _Pending = _Pending.Substring(_Pending.Length - keep);
                        break;
                    }
                }
                else
                {
                    int j = _Pending.IndexOf(_Close, StringComparison.Ordinal);
                    if (j >= 0)
                    {
                        _Thinking.Append(_Pending, 0, j);
                        _Pending = _Pending.Substring(j + _Close.Length);
                        _Inside = false;
                    }
                    else
                    {
                        int keep = PartialTailLength(_Pending, _Close);
                        _Thinking.Append(_Pending, 0, _Pending.Length - keep);
                        _Pending = _Pending.Substring(_Pending.Length - keep);
                        break;
                    }
                }
            }

            LastThinkingDelta = _Thinking.Length > thinkingStart ? _Thinking.ToString(thinkingStart, _Thinking.Length - thinkingStart) : String.Empty;
            string emitted = visible.ToString();
            _Visible.Append(emitted);
            return emitted;
        }

        /// <summary>
        /// Flush held-back text at the end of the response. An unterminated think block is treated as reasoning.
        /// </summary>
        /// <returns>Trailing visible text (may be empty).</returns>
        public string Finish()
        {
            string tail = _Pending;
            _Pending = String.Empty;
            if (_Inside)
            {
                _Thinking.Append(tail);
                _Inside = false;
                LastThinkingDelta = tail;
                return String.Empty;
            }
            LastThinkingDelta = String.Empty;
            _Visible.Append(tail);
            return tail;
        }

        #endregion

        #region Private-Methods

        private static int PartialTailLength(string s, string marker)
        {
            int max = Math.Min(s.Length, marker.Length - 1);
            for (int len = max; len > 0; len--)
            {
                if (String.CompareOrdinal(s, s.Length - len, marker, 0, len) == 0) return len;
            }
            return 0;
        }

        #endregion
    }
}
