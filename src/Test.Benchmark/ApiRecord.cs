namespace Hugo.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>Typed ApiRecord contract for the public API benchmark.</summary>
    public class ApiRecord
    {
        /// <summary>Id value.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Name value.</summary>
        public string Name { get; set; } = String.Empty;
        /// <summary>Version value.</summary>
        public long Version { get; set; } = 0;
        /// <summary>DocumentSetVersion value.</summary>
        public long DocumentSetVersion { get; set; } = 0;
        /// <summary>CurrentRevision value.</summary>
        public int CurrentRevision { get; set; } = 0;
        /// <summary>Markdown value.</summary>
        public string Markdown { get; set; } = String.Empty;
        /// <summary>ContentHash value.</summary>
        public string ContentHash { get; set; } = String.Empty;
        /// <summary>ContentBytes value.</summary>
        public int ContentBytes { get; set; } = 0;
        /// <summary>CompiledText value.</summary>
        public string CompiledText { get; set; } = String.Empty;
        /// <summary>EstimatedTokens value.</summary>
        public long EstimatedTokens { get; set; } = 0;
        /// <summary>Items value.</summary>
        public List<ApiRecord> Items { get; set; } = new List<ApiRecord>();
        /// <summary>Issues value.</summary>
        public List<BenchmarkIssue> Issues { get; set; } = new List<BenchmarkIssue>();
    }
}
