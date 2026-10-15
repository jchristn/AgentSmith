namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Compiled instructions for a profile and surface (preview).
    /// </summary>
    public class CompiledInstructions
    {
        #region Public-Members

        /// <summary>
        /// Profile Identifier.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Profile name.
        /// </summary>
        public string? ProfileName { get; set; } = null;

        /// <summary>
        /// Surface.
        /// </summary>
        public string? Surface { get; set; } = null;

        /// <summary>
        /// Compiled instruction text.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Token estimate.
        /// </summary>
        public long TokenEstimate { get; set; } = 0;

        /// <summary>
        /// Binding count.
        /// </summary>
        public int BindingCount { get; set; } = 0;

        /// <summary>
        /// Segments.
        /// </summary>
        public List<InstructionSegment> Segments { get; set; } = new List<InstructionSegment>();

        /// <summary>
        /// Warnings.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// From cache.
        /// </summary>
        public bool FromCache { get; set; } = false;

        /// <summary>
        /// Compiled UTC.
        /// </summary>
        public DateTime? CompiledUtc { get; set; } = null;

        #endregion
    }
}
