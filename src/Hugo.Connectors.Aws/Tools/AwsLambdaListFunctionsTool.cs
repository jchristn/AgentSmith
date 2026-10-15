namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_lambda_list_functions</c> (Read): lambda:ListFunctions with Marker paging and an optional name filter
    /// applied to each page. Thread-safe.
    /// </summary>
    public class AwsLambdaListFunctionsTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_lambda_list_functions";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsLambdaListFunctionsTool() : base(BuildDescriptor(), AwsServiceNames.Lambda, "lambda:ListFunctions")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            ListFunctionsRequest request = new ListFunctionsRequest();
            request.MaxItems = PageSize(context, arguments, options, 1, 50);
            string? next = arguments.GetString("nextToken");
            if (!String.IsNullOrEmpty(next)) request.Marker = next;
            string? contains = arguments.GetString("nameContains");

            ListFunctionsResponse response = await session.Lambda().ListFunctionsAsync(request, token).ConfigureAwait(false);
            AwsTable table = new AwsTable("Lambda functions", "function", "runtime", "memoryMb", "timeoutS", "state", "lastModified", "handler");
            int skipped = 0;
            foreach (FunctionConfiguration f in response.Functions ?? new List<FunctionConfiguration>())
            {
                if (!String.IsNullOrWhiteSpace(contains) && (f.FunctionName ?? String.Empty).IndexOf(contains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    skipped++;
                    continue;
                }
                table.Add(
                    f.FunctionName,
                    f.Runtime?.Value ?? f.PackageType?.Value,
                    AwsResultBuilder.Number(f.MemorySize),
                    AwsResultBuilder.Number(f.Timeout),
                    f.State?.Value,
                    f.LastModified,
                    f.Handler);
            }
            table.NextToken = response.NextMarker;
            if (skipped > 0) table.Notes.Add(skipped + " functions on this page did not match nameContains; later pages may hold more matches.");
            if (table.Rows.Count == 0) table.Notes.Add("No functions matched on this page.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "List Lambda functions";
            d.Description =
                "List Lambda functions with runtime, memory, timeout, state, last modification, and handler. nameContains "
                + "filters each page by a case-insensitive substring. Paged with nextToken.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"nameContains\":{\"type\":\"string\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":50},"
                + "\"nextToken\":{\"type\":\"string\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of functions and a nextToken when more pages exist.";
            return d;
        }

        #endregion
    }
}
