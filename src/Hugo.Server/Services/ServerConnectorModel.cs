namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Inference;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using PolyPrompt.Models;

    /// <summary>
    /// The server's <see cref="IConnectorModel"/> (P3.26, D-038): connector tools call the calling profile's inference
    /// endpoint (with its fallback) through the model gateway. Calls are metered per tool invocation: after
    /// <see cref="Budget"/> calls, further calls fail with rate_limited. Each call is traced (model &lt;provider&gt;
    /// connector) and counted in hugo_model_connector_requests_total and hugo_model_connector_tokens_total by purpose.
    /// One instance serves one invocation. Thread-safe.
    /// </summary>
    public class ServerConnectorModel : IConnectorModel
    {
        #region Public-Members

        /// <summary>
        /// Calls allowed for this invocation. Minimum 0.
        /// </summary>
        public int Budget { get; }

        /// <summary>
        /// Calls made so far.
        /// </summary>
        public int Calls
        {
            get
            {
                return Volatile.Read(ref _Calls);
            }
        }

        #endregion

        #region Private-Members

        private readonly IModelGateway _Gateway;
        private readonly ModelEndpointConfig _Endpoint;
        private readonly ModelEndpointConfig? _Fallback;
        private readonly SecretScrubber _Scrubber;
        private int _Calls = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="gateway">Model gateway.</param>
        /// <param name="endpoint">Profile inference endpoint.</param>
        /// <param name="fallback">Fallback endpoint, or null.</param>
        /// <param name="budget">Calls allowed for this invocation.</param>
        /// <exception cref="ArgumentNullException">Thrown when gateway or endpoint is null.</exception>
        public ServerConnectorModel(IModelGateway gateway, ModelEndpointConfig endpoint, ModelEndpointConfig? fallback, int budget)
        {
            _Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _Fallback = fallback;
            Budget = Math.Max(0, budget);
            _Scrubber = new SecretScrubber();
            _Scrubber.AddKnownValue(endpoint.ApiKey);
            if (fallback != null) _Scrubber.AddKnownValue(fallback.ApiKey);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ConnectorModelResponse> CompleteAsync(ConnectorModelRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string purpose = String.IsNullOrWhiteSpace(request.Purpose) ? "unspecified" : request.Purpose;
            string provider = _Endpoint.Provider.ToString();
            if (Interlocked.Increment(ref _Calls) > Budget)
            {
                HugoMetrics.Count(HugoMetricCatalog.ConnectorModelRequests, 1, HugoMetrics.Tags("provider", provider, "purpose", purpose, "outcome", "budget_exhausted"));
                ConnectorException exhausted = new ConnectorException(ConnectorErrorCodes.RateLimited, "This tool call has used its model call budget (" + Budget + " calls, Connectors.ModelCallBudgetPerInvocation).");
                throw exhausted;
            }

            ModelChatCall call = new ModelChatCall();
            call.Endpoint = _Endpoint;
            call.Fallback = _Fallback;
            call.Stream = false;
            call.Kind = "connector";
            call.MaxOutputTokens = request.MaxOutputTokens;
            call.Temperature = request.Temperature;
            List<ChatMessage> messages = new List<ChatMessage>();
            if (!String.IsNullOrWhiteSpace(request.SystemPrompt)) messages.Add(ChatMessage.System(request.SystemPrompt));
            messages.Add(ChatMessage.User(request.UserPrompt ?? String.Empty));
            call.Messages = messages;

            using (Activity? activity = HugoTracing.Start("model " + provider + " connector"))
            {
                activity?.SetTag("hugo.purpose", purpose);
                try
                {
                    ModelChatResult result = await _Gateway.ChatAsync(call, token).ConfigureAwait(false);
                    HugoMetrics.Count(HugoMetricCatalog.ConnectorModelRequests, 1, HugoMetrics.Tags("provider", provider, "purpose", purpose, "outcome", "ok"));
                    HugoMetrics.Count(HugoMetricCatalog.ConnectorModelTokens, result.InputTokens, HugoMetrics.Tags("provider", provider, "purpose", purpose, "direction", "input"));
                    HugoMetrics.Count(HugoMetricCatalog.ConnectorModelTokens, result.OutputTokens, HugoMetrics.Tags("provider", provider, "purpose", purpose, "direction", "output"));
                    HugoTracing.Ok(activity);
                    ConnectorModelResponse response = new ConnectorModelResponse();
                    response.Text = result.Text;
                    response.Model = result.Model;
                    response.InputTokens = result.InputTokens;
                    response.OutputTokens = result.OutputTokens;
                    return response;
                }
                catch (ModelEndpointException e)
                {
                    HugoMetrics.Count(HugoMetricCatalog.ConnectorModelRequests, 1, HugoMetrics.Tags("provider", provider, "purpose", purpose, "outcome", "error"));
                    HugoTracing.Fail(activity, e);
                    throw new ConnectorException(e.StatusCode == 429 ? ConnectorErrorCodes.RateLimited : ConnectorErrorCodes.UpstreamError, "The profile's model failed: " + (_Scrubber.Scrub(e.Message) ?? "error"), e);
                }
            }
        }

        #endregion
    }
}
