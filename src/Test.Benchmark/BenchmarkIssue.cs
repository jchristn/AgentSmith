namespace Hugo.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>Typed BenchmarkIssue contract for the public API benchmark.</summary>
    public class BenchmarkIssue
    {
        /// <summary>Code value.</summary>
        public string Code { get; set; } = String.Empty;
        /// <summary>Message value.</summary>
        public string Message { get; set; } = String.Empty;
    }
}
