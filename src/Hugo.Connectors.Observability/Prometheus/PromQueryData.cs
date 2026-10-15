namespace Hugo.Connectors.Observability
{
    using System.Text.Json;

    /// <summary>
    /// Data of a Prometheus or Loki query response. The shape of <see cref="Result"/> depends on
    /// <see cref="ResultType"/> (<c>vector</c>, <c>matrix</c>, <c>scalar</c>, <c>string</c>, or Loki <c>streams</c>), so it
    /// is held as an element and deserialized into the matching typed class.
    /// </summary>
    public class PromQueryData
    {
        /// <summary>
        /// Result type.
        /// </summary>
        public string? ResultType { get; set; } = null;

        /// <summary>
        /// Result, shaped by <see cref="ResultType"/>.
        /// </summary>
        public JsonElement Result { get; set; }
    }
}
