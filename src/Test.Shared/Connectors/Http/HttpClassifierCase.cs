namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One row of the <c>http_request</c> classifier table.
    /// </summary>
    public class HttpClassifierCase
    {
        /// <summary>
        /// Method argument (null means omitted).
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Path argument (null means omitted).
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Resource settings.
        /// </summary>
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Expected result: R, W, D, or X (rejected).
        /// </summary>
        public string Expected { get; set; } = "R";

        /// <summary>
        /// Short name of the settings variant, for display.
        /// </summary>
        public string Variant { get; set; } = String.Empty;
    }
}
