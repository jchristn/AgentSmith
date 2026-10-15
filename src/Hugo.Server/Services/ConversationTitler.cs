namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Inference;
    using PolyPrompt.Models;

    /// <summary>
    /// Generates a conversation title from the first exchange with a short, non-streaming model call (P6.16). When the
    /// call fails or returns nothing usable, the title falls back to the start of the user's message. Thread-safe.
    /// </summary>
    public class ConversationTitler
    {
        #region Public-Members

        /// <summary>
        /// Whether titles are generated with the model. False uses the start of the user's message. Default true.
        /// </summary>
        public bool UseModel { get; set; } = true;

        /// <summary>
        /// Instruction sent as the system message of the title call.
        /// </summary>
        public string Prompt
        {
            get
            {
                return _Prompt;
            }
            set
            {
                _Prompt = String.IsNullOrWhiteSpace(value) ? _DefaultPrompt : value;
            }
        }

        /// <summary>
        /// Longest title kept, in characters. Default 80, minimum 16, maximum 256.
        /// </summary>
        public int MaxChars
        {
            get
            {
                return _MaxChars;
            }
            set
            {
                _MaxChars = Math.Clamp(value, 16, 256);
            }
        }

        /// <summary>
        /// Seconds the title call may take. Default 20, minimum 1, maximum 300.
        /// </summary>
        public int TimeoutSeconds
        {
            get
            {
                return _TimeoutSeconds;
            }
            set
            {
                _TimeoutSeconds = Math.Clamp(value, 1, 300);
            }
        }

        #endregion

        #region Private-Members

        private static readonly string _DefaultPrompt = "Write a short title (at most six words) for the conversation below. Reply with the title only: no quotes, no punctuation at the end, no explanation.";
        private readonly IModelGateway _Gateway;
        private string _Prompt = _DefaultPrompt;
        private int _MaxChars = 80;
        private int _TimeoutSeconds = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="gateway">Model gateway.</param>
        /// <exception cref="ArgumentNullException">Thrown when gateway is null.</exception>
        public ConversationTitler(IModelGateway gateway)
        {
            _Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a title.
        /// </summary>
        /// <param name="endpoint">Inference endpoint, or null to use the fallback title.</param>
        /// <param name="userMessage">First user message.</param>
        /// <param name="answer">First answer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Title result (never empty).</returns>
        public async Task<ConversationTitle> GenerateAsync(ModelEndpointConfig? endpoint, string userMessage, string answer, CancellationToken token)
        {
            ConversationTitle title = new ConversationTitle();
            title.Text = Fallback(userMessage);
            if (!UseModel || endpoint == null) return title;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(_TimeoutSeconds));
                try
                {
                    StringBuilder exchange = new StringBuilder();
                    exchange.Append("User: ").AppendLine(Clip(userMessage ?? String.Empty, 2000));
                    exchange.Append("Assistant: ").AppendLine(Clip(answer ?? String.Empty, 2000));
                    ModelChatCall call = new ModelChatCall();
                    call.Endpoint = endpoint;
                    call.Messages = new List<ChatMessage> { ChatMessage.System(_Prompt), ChatMessage.User(exchange.ToString()) };
                    call.Stream = false;
                    call.MaxOutputTokens = 32;
                    call.Kind = "title";
                    ModelChatResult result = await _Gateway.ChatAsync(call, timeout.Token).ConfigureAwait(false);
                    title.InputTokens = result.InputTokens;
                    title.OutputTokens = result.OutputTokens;
                    string cleaned = Clean(ThinkBlockParser.Strip(result.Text));
                    if (cleaned.Length > 0)
                    {
                        title.Text = cleaned;
                        title.FromModel = true;
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
                {
                }
            }
            return title;
        }

        #endregion

        #region Private-Methods

        private string Fallback(string? userMessage)
        {
            string text = (userMessage ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length == 0) return "New conversation";
            return Clip(text, Math.Min(_MaxChars, 60));
        }

        private string Clean(string text)
        {
            string line = (text ?? String.Empty).Trim();
            int newline = line.IndexOf('\n');
            if (newline >= 0) line = line.Substring(0, newline).Trim();
            line = line.Trim('"', '\'', '`', '*', '#', ' ');
            if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)) line = line.Substring(6).Trim();
            line = line.TrimEnd('.', '!', ' ');
            return Clip(line, _MaxChars);
        }

        private static string Clip(string text, int max)
        {
            if (text.Length <= max) return text;
            return text.Substring(0, max).TrimEnd() + "...";
        }

        #endregion
    }
}
