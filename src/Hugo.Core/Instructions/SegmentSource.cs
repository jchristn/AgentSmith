namespace Hugo.Core.Instructions
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Origin of one marked range while the instruction compiler renders; resolved into an
    /// <see cref="InstructionSegment"/> once offsets are known. Not thread-safe.
    /// </summary>
    public class SegmentSource
    {
        /// <summary>
        /// Template key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Layer.
        /// </summary>
        public PromptLayerEnum Layer { get; set; } = PromptLayerEnum.BuiltIn;

        /// <summary>
        /// Version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Binding alias, when per resource.
        /// </summary>
        public string? BindingAlias { get; set; } = null;
    }
}
