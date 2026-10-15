namespace Test.Shared.Integration.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A tools/list result as the tests read it.
    /// </summary>
    public class McpTestToolList
    {
        /// <summary>
        /// Tools.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<McpTestTool> Tools { get; set; } = new List<McpTestTool>();

        /// <summary>
        /// A tool by name, or null.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Tool or null.</returns>
        public McpTestTool? Find(string name)
        {
            return Tools.FirstOrDefault(t => String.Equals(t.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Tool names.
        /// </summary>
        [JsonIgnore]
        public List<string> Names
        {
            get
            {
                return Tools.Select(t => t.Name).ToList();
            }
        }
    }
}
