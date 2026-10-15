namespace Test.Shared.Agent
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;

    /// <summary>
    /// How <see cref="ScriptedToolExecutor"/> handles calls to one tool.
    /// </summary>
    public class ScriptedToolBehavior
    {
        /// <summary>
        /// Decision to report: Allowed (execute), Denied, ApprovalRequired (wait for <see cref="ApprovalSignal"/>
        /// when <see cref="WaitForApproval"/> is true, otherwise return pending).
        /// </summary>
        public AgentToolDecisionEnum Decision { get; set; } = AgentToolDecisionEnum.Allowed;

        /// <summary>
        /// Effect to report.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Output text when executed.
        /// </summary>
        public string Output { get; set; } = "ok";

        /// <summary>
        /// Milliseconds the execution takes (honoring cancellation).
        /// </summary>
        public int DelayMs { get; set; } = 0;

        /// <summary>
        /// Whether an approval-required call blocks for a decision (true) or returns pending (false).
        /// </summary>
        public bool WaitForApproval { get; set; } = true;

        /// <summary>
        /// Completed by the test with Approved, Rejected, or Expired.
        /// </summary>
        public TaskCompletionSource<AgentToolDecisionEnum> ApprovalSignal { get; set; } = new TaskCompletionSource<AgentToolDecisionEnum>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Approval ID to report.
        /// </summary>
        public string ApprovalId { get; set; } = "apr_test";
    }
}
