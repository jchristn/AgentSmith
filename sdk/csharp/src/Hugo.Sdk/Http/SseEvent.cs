namespace Hugo.Sdk.Http
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// One server-sent event.
    /// </summary>
    public class SseEvent
    {
        #region Public-Members

        /// <summary>
        /// Event name (the event field), for example stream.opened, heartbeat, secret.created, or assistant.delta.
        /// Null when the server sent no event field (the SSE default type is message).
        /// </summary>
        public string? Event { get; set; } = null;

        /// <summary>
        /// Event identifier (the id field), or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Event data. Multiple data lines are joined with a line feed.
        /// </summary>
        public string Data { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize the data as JSON using the SDK's serializer options.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <returns>Deserialized value, or default when the data is empty.</returns>
        /// <exception cref="JsonException">Thrown when the data is not valid JSON for the type.</exception>
        public T? DataAs<T>()
        {
            if (String.IsNullOrWhiteSpace(Data)) return default;
            return JsonSerializer.Deserialize<T>(Data, HugoTransport.JsonOptions);
        }

        /// <summary>
        /// Text form for logging.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            return (Event ?? "message") + (Id != null ? " #" + Id : String.Empty) + ": " + Data;
        }

        #endregion
    }
}
