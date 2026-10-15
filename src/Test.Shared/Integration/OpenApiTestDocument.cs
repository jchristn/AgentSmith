namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The parts of /openapi.json the route contract tests read: each path's operations and their declared responses.
    /// </summary>
    public class OpenApiTestDocument
    {
        /// <summary>
        /// Operations by path, then by lower-case HTTP method.
        /// </summary>
        public Dictionary<string, Dictionary<string, OpenApiTestOperation>> Paths { get; set; } = new Dictionary<string, Dictionary<string, OpenApiTestOperation>>();

        /// <summary>
        /// Declared status codes of one operation, or an empty list when the path or method is absent.
        /// </summary>
        /// <param name="path">Path template.</param>
        /// <param name="method">HTTP method, any case.</param>
        /// <returns>Status codes as declared (for example 200, 201).</returns>
        public List<string> Statuses(string path, string method)
        {
            if (!Paths.TryGetValue(path, out Dictionary<string, OpenApiTestOperation>? operations)) return new List<string>();
            if (!operations.TryGetValue(method.ToLowerInvariant(), out OpenApiTestOperation? operation)) return new List<string>();
            return new List<string>(operation.Responses.Keys);
        }
    }
}
