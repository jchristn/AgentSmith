namespace Hugo.Sdk.Models
{
    using System;
    using System.Text.Json;
    using Hugo.Sdk.Http;

    /// <summary>
    /// One event of a streamed chat turn (preview). Type is one of the values in ChatEventTypes; Data is the raw JSON
    /// payload, which DataAs deserializes into a type of your choice while the payload shapes settle.
    /// </summary>
    public class ChatEvent
    {
        #region Public-Members

        /// <summary>
        /// Event type, for example assistant.delta or turn.completed.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Event identifier, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Raw JSON payload.
        /// </summary>
        public string Data { get; set; } = String.Empty;

        /// <summary>
        /// Whether the event ends the turn (turn.completed or error).
        /// </summary>
        public bool IsTerminal
        {
            get
            {
                return Type == ChatEventTypes.TurnCompleted || Type == ChatEventTypes.Error;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ChatEvent()
        {
        }

        /// <summary>
        /// Build a chat event from a server-sent event.
        /// </summary>
        /// <param name="item">Server-sent event.</param>
        /// <returns>Chat event.</returns>
        /// <exception cref="ArgumentNullException">Thrown when item is null.</exception>
        public static ChatEvent FromSse(SseEvent item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            ChatEvent chat = new ChatEvent();
            chat.Type = item.Event ?? "message";
            chat.Id = item.Id;
            chat.Data = item.Data;
            return chat;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize the payload.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <returns>Value, or default when the payload is empty.</returns>
        /// <exception cref="JsonException">Thrown when the payload is not valid JSON for the type.</exception>
        public T? DataAs<T>()
        {
            if (String.IsNullOrWhiteSpace(Data)) return default;
            return JsonSerializer.Deserialize<T>(Data, HugoTransport.JsonOptions);
        }

        #endregion
    }
}
