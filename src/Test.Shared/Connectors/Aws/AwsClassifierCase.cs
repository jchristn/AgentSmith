namespace Test.Shared.Connectors.Aws
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One row of the AWS classification table: a tool call and the classification it must get.
    /// </summary>
    public class AwsClassifierCase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Tool { get; set; } = String.Empty;

        /// <summary>
        /// Arguments JSON.
        /// </summary>
        public string Arguments { get; set; } = "{}";

        /// <summary>
        /// Expected effect (ignored when <see cref="Rejected"/> is true).
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Whether the call must be rejected.
        /// </summary>
        public bool Rejected { get; set; } = false;

        /// <summary>
        /// Whether the call must always require approval.
        /// </summary>
        public bool AlwaysRequireApproval { get; set; } = false;

        /// <summary>
        /// Text the approval summary must contain, or null.
        /// </summary>
        public string? SummaryContains { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <param name="arguments">Arguments JSON.</param>
        /// <param name="effect">Expected effect.</param>
        /// <param name="summaryContains">Expected summary text, or null.</param>
        public AwsClassifierCase(string tool, string arguments, EffectEnum effect, string? summaryContains = null)
        {
            Tool = tool;
            Arguments = arguments;
            Effect = effect;
            SummaryContains = summaryContains;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A case that must be rejected.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <param name="arguments">Arguments JSON.</param>
        /// <returns>Case.</returns>
        public static AwsClassifierCase Reject(string tool, string arguments)
        {
            return new AwsClassifierCase(tool, arguments, EffectEnum.Destructive) { Rejected = true };
        }

        /// <summary>
        /// Describe the case.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            return Tool + " " + Arguments;
        }

        #endregion
    }
}
