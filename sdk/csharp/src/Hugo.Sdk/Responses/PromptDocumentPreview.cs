namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>PromptDocumentPreview contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocumentPreview
    {
        /// <summary>CompiledText.</summary>
        public string CompiledText { get; set; } = String.Empty;

        /// <summary>Segments.</summary>
        public List<InstructionSegment> Segments { get; set; } = new List<InstructionSegment>();

        /// <summary>Manifest.</summary>
        public List<AgentPromptDocument> Manifest { get; set; } = new List<AgentPromptDocument>();

        /// <summary>Issues.</summary>
        public List<PromptDocumentPreviewIssue> Issues { get; set; } = new List<PromptDocumentPreviewIssue>();

        /// <summary>DocumentSetVersion.</summary>
        public long DocumentSetVersion { get; set; } = 0;

        /// <summary>InlineBytes.</summary>
        public long InlineBytes { get; set; } = 0;

        /// <summary>ManifestBytes.</summary>
        public long ManifestBytes { get; set; } = 0;

        /// <summary>EstimatedTokens.</summary>
        public long EstimatedTokens { get; set; } = 0;

        /// <summary>Audience.</summary>
        public string Audience { get; set; } = "Authenticated";

    }
}
