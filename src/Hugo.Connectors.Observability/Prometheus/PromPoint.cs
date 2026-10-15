namespace Hugo.Connectors.Observability
{
    using System;
    using System.Globalization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One sample of a Prometheus series, serialized by the API as <c>[unixSeconds, "value"]</c>.
    /// </summary>
    [JsonConverter(typeof(PromPointConverter))]
    public class PromPoint
    {
        #region Public-Members

        /// <summary>
        /// Unix time in seconds, with fraction.
        /// </summary>
        public double Timestamp { get; set; } = 0;

        /// <summary>
        /// Value as sent by Prometheus (it may be <c>NaN</c>, <c>+Inf</c>, or <c>-Inf</c>).
        /// </summary>
        public string Value { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse the value as a number.
        /// </summary>
        /// <returns>Number, or NaN when it cannot be parsed.</returns>
        public double Number()
        {
            if (Value == "+Inf") return Double.PositiveInfinity;
            if (Value == "-Inf") return Double.NegativeInfinity;
            return Double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : Double.NaN;
        }

        #endregion
    }
}
