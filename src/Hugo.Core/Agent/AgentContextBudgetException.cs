namespace Hugo.Core.Agent
{
    using System;

    /// <summary>Safe pre-provider rejection when assembled document context exceeds an endpoint's token budget.</summary>
    public sealed class AgentContextBudgetException : Exception
    {
        /// <summary>Create an error without including prompt content.</summary>
        public AgentContextBudgetException() : base("Attached documents and conversation context exceed the model's context budget after reserving output tokens. Reduce Inline content, read smaller Reference portions, or choose a larger-context model.") { }
    }
}
