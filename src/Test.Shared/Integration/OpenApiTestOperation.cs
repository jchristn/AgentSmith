namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One OpenAPI operation as the route contract tests read it.
    /// </summary>
    public class OpenApiTestOperation
    {
        /// <summary>
        /// Declared responses by status code; the response bodies are not inspected.
        /// </summary>
        public Dictionary<string, object?> Responses { get; set; } = new Dictionary<string, object?>();
    }
}
