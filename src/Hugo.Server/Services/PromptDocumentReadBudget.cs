namespace Hugo.Server.Services
{
    /// <summary>Request-turn-local byte accounting, retained weakly with its turn.</summary>
    public class PromptDocumentReadBudget
    {
        /// <summary>Already returned UTF-8 JSON bytes.</summary>
        public long Bytes { get; set; }
    }
}
