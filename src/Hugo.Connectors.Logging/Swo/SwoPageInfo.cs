namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// SolarWinds Observability paging information (<c>Common.PageInfo</c>).
    /// </summary>
    public class SwoPageInfo
    {
        /// <summary>
        /// Token (or link) for the previous page; empty on the first page.
        /// </summary>
        [JsonPropertyName("prevPage")]
        public string? PrevPage { get; set; } = null;

        /// <summary>
        /// Token (or link) for the next page; empty on the last page.
        /// </summary>
        [JsonPropertyName("nextPage")]
        public string? NextPage { get; set; } = null;
    }
}
