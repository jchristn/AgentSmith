namespace Hugo.Core.Security
{
    /// <summary>One original text segment or a generated, already sanitized pattern replacement.</summary>
    internal class ScrubSegment
    {
        internal string Text { get; }
        internal bool ProtectedText { get; }

        internal ScrubSegment(string text, bool protectedText)
        {
            Text = text;
            ProtectedText = protectedText;
        }
    }
}
