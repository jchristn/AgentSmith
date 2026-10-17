namespace Hugo.Core.Responses
{
    /// <summary>Visitor feedback on one visible assistant message.</summary>
    public class StandaloneChatFeedback
    {
        /// <summary>Positive, Negative, or null for comment only or clearing a rating.</summary>
        public string? Rating { get; set; } = null;

        /// <summary>Optional free text, at most 4000 characters. Empty clears a prior comment.</summary>
        public string Comment { get; set; } = System.String.Empty;
    }
}
