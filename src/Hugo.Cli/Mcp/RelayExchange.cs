namespace Hugo.Cli.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// One message the relay forwards to the server, with what it learned from the message to set HTTP headers and
    /// to recognize the matching response. Not thread-safe.
    /// </summary>
    public class RelayExchange
    {
        #region Public-Members

        /// <summary>
        /// The JSON text posted to the server.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// The parsed message, or null for a batch.
        /// </summary>
        public JsonRpcMessage? Message { get; set; } = null;

        /// <summary>
        /// Whether the body is a JSON-RPC batch (an array, 2025-03-26).
        /// </summary>
        public bool IsBatch { get; set; } = false;

        /// <summary>
        /// Method, or null for responses and batches.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// The ID sent to the server as raw JSON text, or null.
        /// </summary>
        public string? IdKey { get; set; } = null;

        /// <summary>
        /// The ID the client used, written back into the response when the request was re-issued with a new ID; null
        /// otherwise.
        /// </summary>
        public JsonElement? OriginalId { get; set; } = null;

        /// <summary>
        /// Whether this is an <c>initialize</c> request.
        /// </summary>
        public bool IsInitialize { get; set; } = false;

        /// <summary>
        /// The protocol version named in <c>params._meta</c> (stateless revision), or null.
        /// </summary>
        public string? StatelessVersion { get; set; } = null;

        /// <summary>
        /// The <c>Mcp-Name</c> routing value (tool name, prompt name, or resource URI), or null.
        /// </summary>
        public string? RoutingName { get; set; } = null;

        /// <summary>
        /// Tool arguments of <c>tools/call</c>, or null.
        /// </summary>
        public Dictionary<string, JsonElement>? Arguments { get; set; } = null;

        /// <summary>
        /// Whether the relay itself sent this message (session re-initialization); its response is not written.
        /// </summary>
        public bool Internal { get; set; } = false;

        /// <summary>
        /// Whether this is a re-issue of a request whose response stream broke.
        /// </summary>
        public bool Reissued { get; set; } = false;

        /// <summary>
        /// Whether a response is expected (the message is a request).
        /// </summary>
        public bool ExpectsResponse
        {
            get
            {
                return Message != null && Message.IsRequest;
            }
        }

        /// <summary>
        /// Short description for diagnostics, for example <c>tools/call #3</c>.
        /// </summary>
        public string Describe
        {
            get
            {
                if (IsBatch) return "batch";
                if (Message == null) return "message";
                if (Message.IsResponse) return "response " + (IdKey ?? "(no id)");
                return Method + (IdKey != null ? " " + IdKey : String.Empty);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build an exchange from a parsed message.
        /// </summary>
        /// <param name="body">JSON text to post.</param>
        /// <param name="message">Parsed message.</param>
        /// <returns>Exchange.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static RelayExchange FromMessage(string body, JsonRpcMessage message)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (message == null) throw new ArgumentNullException(nameof(message));
            RelayExchange exchange = new RelayExchange();
            exchange.Body = body;
            exchange.Message = message;
            exchange.Method = message.Method;
            exchange.IdKey = message.IdKey;
            exchange.IsInitialize = message.IsRequest && String.Equals(message.Method, "initialize", StringComparison.Ordinal);

            McpRequestParams? parameters = CliJson.TryDeserialize<McpRequestParams>(message.Params);
            if (parameters != null)
            {
                exchange.StatelessVersion = String.IsNullOrEmpty(parameters.Meta?.ProtocolVersion) ? null : parameters.Meta!.ProtocolVersion;
                if (String.Equals(message.Method, "tools/call", StringComparison.Ordinal))
                {
                    exchange.RoutingName = parameters.Name;
                    exchange.Arguments = parameters.Arguments;
                }
                else if (String.Equals(message.Method, "prompts/get", StringComparison.Ordinal))
                {
                    exchange.RoutingName = parameters.Name;
                }
                else if (String.Equals(message.Method, "resources/read", StringComparison.Ordinal))
                {
                    exchange.RoutingName = parameters.Uri;
                }
            }
            return exchange;
        }

        /// <summary>
        /// A copy of this request with a new ID, for re-issuing after the response stream broke (stateless revision).
        /// The response is written back with the client's original ID.
        /// </summary>
        /// <returns>Exchange.</returns>
        /// <exception cref="InvalidOperationException">Thrown when this exchange is not a request.</exception>
        public RelayExchange Reissue()
        {
            if (Message == null || !Message.IsRequest) throw new InvalidOperationException("Only requests can be re-issued.");
            JsonRpcMessage copy = new JsonRpcMessage();
            copy.JsonRpc = Message.JsonRpc;
            copy.Method = Message.Method;
            copy.Params = Message.Params;
            copy.Extra = Message.Extra;
            copy.Id = JsonSerializer.SerializeToElement("hugo-cli-" + Guid.NewGuid().ToString("N"));

            RelayExchange again = FromMessage(copy.ToJson(), copy);
            again.OriginalId = OriginalId ?? Message.Id;
            again.Reissued = true;
            again.Internal = Internal;
            return again;
        }

        #endregion
    }
}
