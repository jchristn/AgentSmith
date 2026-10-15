namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda;
    using Amazon.Lambda.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_lambda_invoke</c> (Write): lambda:InvokeFunction with a bounded JSON payload, synchronously (returning the
    /// bounded response payload and the log tail) or asynchronously (Event). Thread-safe.
    /// </summary>
    public class AwsLambdaInvokeTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_lambda_invoke";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsLambdaInvokeTool() : base(BuildDescriptor(), AwsServiceNames.Lambda, "lambda:InvokeFunction")
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The payload text of a call: the <c>payload</c> argument as JSON (an object, array, or a string holding JSON), or
        /// <c>{}</c> when absent.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <returns>JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when arguments is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when a string payload is not JSON.</exception>
        public static string PayloadOf(ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? raw = arguments.GetRawJson("payload");
            if (raw == null) return "{}";
            if (raw.Length > 0 && raw[0] == '"')
            {
                string inner = arguments.GetString("payload") ?? String.Empty;
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(inner))
                    {
                    }
                }
                catch (JsonException e)
                {
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "payload is a string but not valid JSON: " + e.Message);
                }
                return inner;
            }
            return raw;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCore(ConnectorInvocationContext context, ToolArguments arguments, AwsOptions options)
        {
            string function = (arguments.GetString("functionName") ?? String.Empty).Trim();
            if (function.Length == 0) return EffectClassification.Reject("functionName is required");
            string payload;
            try
            {
                payload = PayloadOf(arguments);
            }
            catch (ConnectorException e)
            {
                return EffectClassification.Reject(e.Message);
            }
            int bytes = Encoding.UTF8.GetByteCount(payload);
            if (bytes > options.LambdaMaxPayloadBytes)
                return EffectClassification.Reject("payload is " + bytes + " bytes; this resource allows at most " + options.LambdaMaxPayloadBytes + " (lambdaMaxPayloadBytes)");

            string qualifier = arguments.GetString("qualifier") ?? String.Empty;
            bool async = String.Equals(arguments.GetString("invocationType"), "Event", StringComparison.OrdinalIgnoreCase);
            string summary = "Invoke Lambda function " + function + (qualifier.Length > 0 ? ":" + qualifier : String.Empty)
                + (async ? " asynchronously" : String.Empty) + " with a " + bytes.ToString(CultureInfo.InvariantCulture) + "-byte payload "
                + Shorten(payload, 160) + " in " + Where(context);
            return new EffectClassification(EffectEnum.Write, "lambda:InvokeFunction runs code that may change state (Invoke is Write)", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            string function = arguments.GetRequiredString("functionName").Trim();
            string payload = PayloadOf(arguments);
            int bytes = Encoding.UTF8.GetByteCount(payload);
            if (bytes > options.LambdaMaxPayloadBytes)
                throw new ConnectorException(ConnectorErrorCodes.TooLarge, "The payload is " + bytes + " bytes; this resource allows at most " + options.LambdaMaxPayloadBytes + " bytes (lambdaMaxPayloadBytes).");

            bool async = String.Equals(arguments.GetString("invocationType"), "Event", StringComparison.OrdinalIgnoreCase);
            bool tail = !async && arguments.GetBool("includeLogTail", true);
            InvokeResponse response;
            using (MemoryStream body = new MemoryStream(Encoding.UTF8.GetBytes(payload)))
            {
                InvokeRequest request = new InvokeRequest();
                request.FunctionName = function;
                request.PayloadStream = body;
                request.InvocationType = async ? InvocationType.Event : InvocationType.RequestResponse;
                if (tail) request.LogType = LogType.Tail;
                string? qualifier = arguments.GetString("qualifier");
                if (!String.IsNullOrWhiteSpace(qualifier)) request.Qualifier = qualifier.Trim();
                response = await session.Lambda().InvokeAsync(request, token).ConfigureAwait(false);
            }

            string responseText = String.Empty;
            if (response.Payload != null)
            {
                using (StreamReader reader = new StreamReader(response.Payload, Encoding.UTF8))
                {
                    responseText = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                }
            }

            AwsTable table = new AwsTable("Lambda " + function + (async ? " invoked asynchronously" : " invoked"));
            table.Details["functionName"] = function;
            table.Details["statusCode"] = AwsResultBuilder.Number(response.StatusCode);
            table.Details["executedVersion"] = response.ExecutedVersion;
            table.Details["functionError"] = String.IsNullOrEmpty(response.FunctionError) ? null : response.FunctionError;
            table.Details["requestBytes"] = bytes.ToString(CultureInfo.InvariantCulture);
            table.Details["responseBytes"] = Encoding.UTF8.GetByteCount(responseText).ToString(CultureInfo.InvariantCulture);

            StringBuilder trailer = new StringBuilder();
            int half = Math.Max(1024, context.Limits.MaxBytes / 2);
            if (!async)
            {
                ToolResult cut = new ToolResult();
                string shown = OutputLimiter.Head(responseText, half, context.Limits.MaxLines / 2, cut);
                if (cut.Truncated) table.Truncated = true;
                table.Details["responsePayload"] = shown.Length == 0 ? "(empty)" : shown;
            }
            if (!String.IsNullOrEmpty(response.LogResult))
            {
                string log;
                try
                {
                    log = Encoding.UTF8.GetString(Convert.FromBase64String(response.LogResult));
                }
                catch (FormatException)
                {
                    log = response.LogResult;
                }
                ToolResult cutLog = new ToolResult();
                trailer.Append("Log tail (last 4 KB):\n").Append(OutputLimiter.Tail(log, Math.Max(1024, context.Limits.MaxBytes / 4), 100, cutLog)).Append('\n');
            }
            if (!String.IsNullOrEmpty(response.FunctionError)) table.Notes.Add("The function returned an error (" + response.FunctionError + "); see the response payload and log tail.");
            if (async) table.Notes.Add("Asynchronous invocation was queued; results are not returned. Check the function's logs with aws_logs_tail on /aws/lambda/" + function + ".");
            table.Trailer = trailer.ToString();
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Invoke Lambda function";
            d.Description =
                "Invoke a Lambda function with a JSON payload. Synchronous calls (default) return the response payload and the "
                + "last 4 KB of the function log; invocationType Event queues the call and returns at once. This is Write and "
                + "usually needs approval because the function may change state; state the function and payload first. The payload "
                + "size is capped by the resource.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"functionName\":{\"type\":\"string\",\"minLength\":1,\"description\":\"Function name or ARN.\"},"
                + "\"payload\":{\"description\":\"JSON payload (object, array, or a string containing JSON). Default {}.\"},"
                + "\"qualifier\":{\"type\":\"string\",\"description\":\"Version or alias.\"},"
                + "\"invocationType\":{\"type\":\"string\",\"enum\":[\"RequestResponse\",\"Event\"],\"description\":\"Default RequestResponse.\"},"
                + "\"includeLogTail\":{\"type\":\"boolean\",\"description\":\"Return the log tail of a synchronous call. Default true.\"}"
                + "},\"required\":[\"functionName\"],\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Write;
            d.OutputDescription = "Status code, function error, bounded response payload, and log tail.";
            return d;
        }

        #endregion
    }
}
