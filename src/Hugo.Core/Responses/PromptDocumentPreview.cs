namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentPreview contract for managed profile documents.</summary>
    public class PromptDocumentPreview
    {
        /// <summary>CompiledText; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string CompiledText { get; set; } = String.Empty;

        /// <summary>Segments; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public List<Hugo.Core.Instructions.InstructionSegment> Segments { get; set; } = new List<Hugo.Core.Instructions.InstructionSegment>();

        /// <summary>Manifest; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public List<AgentPromptDocumentView> Manifest { get; set; } = new List<AgentPromptDocumentView>();

        /// <summary>Issues; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public List<PromptDocumentIssue> Issues { get; set; } = new List<PromptDocumentIssue>();

        /// <summary>DocumentSetVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long DocumentSetVersion { get; set; } = 0;

        /// <summary>InlineBytes; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int InlineBytes { get; set; } = 0;

        /// <summary>ManifestBytes; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int ManifestBytes { get; set; } = 0;

        /// <summary>EstimatedTokens; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long EstimatedTokens { get; set; } = 0;

        /// <summary>Audience; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Audience { get; set; } = "Authenticated";

    }
}
