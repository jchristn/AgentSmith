namespace Test.Shared.Agent.StubLlm
{
    using System;

    /// <summary>
    /// A tool call the stub LLM returns.
    /// </summary>
    public class StubToolCall
    {
        /// <summary>
        /// Call ID. Generated when empty.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as a JSON object string.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Instantiate.
        /// </summary>
        public StubToolCall()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="argumentsJson">Arguments JSON.</param>
        public StubToolCall(string name, string argumentsJson)
        {
            Name = name ?? String.Empty;
            ArgumentsJson = String.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        }
    }
}
