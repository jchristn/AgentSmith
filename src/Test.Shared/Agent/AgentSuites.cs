namespace Test.Shared.Agent
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>
    /// Agent runtime and inference suites (Phase 6). Everything runs against the in-process stub LLM, so the suites
    /// always run and need no model.
    /// </summary>
    public static class AgentSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor("agent.units", "Agent building blocks: think parser, wrapper, locks, retry, breaker, gate, factory", AgentUnitCases.Build()),
                new TestSuiteDescriptor("agent.inference", "Model gateway against the stub LLM: streaming, retries, fallback, circuit breaker, embeddings, validation", AgentInferenceCases.Build()),
                new TestSuiteDescriptor("agent.runtime", "Agent runtime turns against the stub LLM", AgentRuntimeCases.Build()),
                new TestSuiteDescriptor(ToolRetrievalScaleCases.Suite, "Tool retrieval on a profile with more than 100 tools (P13.35)", ToolRetrievalScaleCases.Build())
            };
        }
    }
}
