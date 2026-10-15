// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/ContextQualitySignal.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;

    /// <summary>
    /// One gap or note about how ready the durable context is for agents. Not thread-safe for mutation.
    /// </summary>
    public class ContextQualitySignal
    {
        /// <summary>
        /// Signal key (for example <c>database_context</c>, <c>table_context</c>, <c>relationships</c>).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Severity: <c>info</c>, <c>warning</c>, or <c>blocker</c>.
        /// </summary>
        public string Severity { get; set; } = "info";

        /// <summary>
        /// What is missing or weak.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// What to do about it.
        /// </summary>
        public string Recommendation { get; set; } = String.Empty;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ContextQualitySignal()
        {
        }
    }
}
