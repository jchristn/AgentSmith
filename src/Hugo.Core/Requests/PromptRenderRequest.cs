namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Render a template with sample data. The sample context is user-defined JSON and intentionally schemaless.
    /// </summary>
    public class PromptRenderRequest
    {
        /// <summary>
        /// Template content for ad hoc renders; ignored when rendering a stored template.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Sample context values merged over the built-in sample context (schemaless by design).
        /// </summary>
        public System.Text.Json.JsonElement? Context { get; set; } = null;

        /// <summary>
        /// Optional deployment whose real context (without secrets) is used as the base.
        /// </summary>
        public string? DeploymentId { get; set; } = null;
    }
}
