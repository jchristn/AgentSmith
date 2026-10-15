namespace Hugo.Core.Instructions
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// The compiled instructions of a profile for one caller and surface (plan sections 10.4 and 10.5). Not thread-safe.
    /// </summary>
    public class CompiledInstructions
    {
        #region Public-Members

        /// <summary>
        /// Profile identifier.
        /// </summary>
        public string ProfileId
        {
            get
            {
                return _ProfileId;
            }
            set
            {
                _ProfileId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Profile name.
        /// </summary>
        public string ProfileName
        {
            get
            {
                return _ProfileName;
            }
            set
            {
                _ProfileName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Surface compiled for.
        /// </summary>
        public InvocationSurfaceEnum Surface { get; set; } = InvocationSurfaceEnum.Chat;

        /// <summary>
        /// Instruction text.
        /// </summary>
        public string Text
        {
            get
            {
                return _Text;
            }
            set
            {
                _Text = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Estimated tokens.
        /// </summary>
        public long TokenEstimate { get; set; } = 0;

        /// <summary>
        /// Resources included.
        /// </summary>
        public int BindingCount { get; set; } = 0;

        /// <summary>
        /// Source map.
        /// </summary>
        public List<InstructionSegment> Segments
        {
            get
            {
                return _Segments;
            }
            set
            {
                _Segments = value ?? new List<InstructionSegment>();
            }
        }

        /// <summary>
        /// Problems found while compiling (for example a template that failed to render and fell back).
        /// </summary>
        public List<string> Warnings
        {
            get
            {
                return _Warnings;
            }
            set
            {
                _Warnings = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Whether the result came from the compile cache.
        /// </summary>
        public bool FromCache { get; set; } = false;

        /// <summary>
        /// When it was compiled (UTC).
        /// </summary>
        public DateTime CompiledUtc
        {
            get
            {
                return _CompiledUtc;
            }
            set
            {
                _CompiledUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _ProfileId = String.Empty;
        private string _ProfileName = String.Empty;
        private string _Text = String.Empty;
        private List<InstructionSegment> _Segments = new List<InstructionSegment>();
        private List<string> _Warnings = new List<string>();
        private DateTime _CompiledUtc = DateTime.UtcNow;

        #endregion
    }
}
