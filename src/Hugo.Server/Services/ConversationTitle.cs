namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// A generated conversation title and the tokens the title call used. Not thread-safe.
    /// </summary>
    public class ConversationTitle
    {
        /// <summary>
        /// Title text.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Whether the model produced the title (false: the start of the user's message).
        /// </summary>
        public bool FromModel { get; set; } = false;

        /// <summary>
        /// Input tokens of the title call.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens of the title call.
        /// </summary>
        public long OutputTokens { get; set; } = 0;
    }
}
