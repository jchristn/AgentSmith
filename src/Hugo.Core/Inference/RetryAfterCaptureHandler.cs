namespace Hugo.Core.Inference
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// HTTP handler placed beneath every PolyPrompt client built by <see cref="ModelClientFactory"/>. It records
    /// the status code and the <c>Retry-After</c> header of each response into <see cref="ModelCallContext.Current"/>,
    /// so the gateway's retry policy can honor Retry-After although PolyPrompt does not expose headers.
    /// Thread-safe.
    /// </summary>
    public class RetryAfterCaptureHandler : DelegatingHandler
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate over an inner handler.
        /// </summary>
        /// <param name="innerHandler">Inner handler.</param>
        /// <exception cref="ArgumentNullException">Thrown when innerHandler is null.</exception>
        public RetryAfterCaptureHandler(HttpMessageHandler innerHandler) : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
        {
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Send the request and capture the status and Retry-After.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            ModelCallContext? context = ModelCallContext.Current;
            if (context != null)
            {
                context.LastStatusCode = (int)response.StatusCode;
                context.RetryAfterSeconds = ParseRetryAfter(response);
            }
            return response;
        }

        #endregion

        #region Private-Methods

        private static double? ParseRetryAfter(HttpResponseMessage response)
        {
            try
            {
                if (response.Headers.RetryAfter == null) return null;
                if (response.Headers.RetryAfter.Delta.HasValue) return Math.Max(0, response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    double seconds = (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
                    return Math.Max(0, seconds);
                }
            }
            catch (FormatException)
            {
            }
            return null;
        }

        #endregion
    }
}
