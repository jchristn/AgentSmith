namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.Mcp.Tools;

    /// <summary>
    /// External MCP server connector (type <c>mcp</c>, family <c>mcp</c>). One resource is one remote MCP server reached
    /// over Streamable HTTP or, when both the server and the resource allow it, a Stdio child process. Because
    /// <see cref="IConnector.Tools"/> is fixed for the life of the process while each server's tools are discovered per
    /// resource, the connector exposes a small fixed tool set: <c>mcp_list_tools</c> (Read), <c>mcp_call_tool</c>
    /// (classified per call from the remote tool's annotations), <c>mcp_list_resources</c> (Read), and
    /// <c>mcp_read_resource</c> (Read). Thread-safe.
    /// </summary>
    public class McpConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "mcp";

        /// <summary>
        /// Tool family.
        /// </summary>
        public static readonly string FamilyName = "mcp";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Options shared with the tools. Never null.
        /// </summary>
        public McpConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with default options (Stdio disabled).
        /// </summary>
        public McpConnector() : this(new McpConnectorOptions())
        {
        }

        /// <summary>
        /// Instantiate with options. The host passes its <c>Connectors.Mcp.AllowStdio</c> setting as
        /// <see cref="McpConnectorOptions.AllowStdio"/>.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public McpConnector(McpConnectorOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new McpListToolsTool(Options),
                new McpCallToolTool(Options),
                new McpListResourcesTool(Options),
                new McpReadResourceTool(Options)
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            ConnectorValidationResult result = new ConnectorValidationResult();
            ResourceSettings s = resource.Settings;

            if (!McpResourceOptions.TryParseTransport(s.GetString(McpSettingKeys.Transport), out McpTransportEnum transport))
            {
                result.Add(McpSettingKeys.Transport, "invalid_value", "transport must be StreamableHttp or Stdio.");
            }
            else if (transport == McpTransportEnum.StreamableHttp)
            {
                if (!McpResourceOptions.TryParseUrl(s.GetString(McpSettingKeys.Url), out Uri? _, out string urlError))
                    result.Add(McpSettingKeys.Url, String.IsNullOrWhiteSpace(s.GetString(McpSettingKeys.Url)) ? "required" : "invalid_format", urlError);
            }
            else
            {
                if (String.IsNullOrWhiteSpace(s.GetString(McpSettingKeys.Command)))
                    result.Add(McpSettingKeys.Command, "required", "The Stdio transport needs command, the executable that runs the MCP server.");
                if (!s.GetBool(McpSettingKeys.AllowStdio, false))
                    result.Add(McpSettingKeys.AllowStdio, "invalid_value", "Set allowStdio to true to use the Stdio transport. It starts a process inside the Hugo host.");
                if (!Options.AllowStdio)
                    result.Add(McpSettingKeys.Transport, "not_allowed", "Stdio MCP servers are disabled on this Hugo server (Connectors.Mcp.AllowStdio is false).");
            }

            ValidateBool(s, result, McpSettingKeys.AllowStdio);
            ValidateRange(s, result, McpSettingKeys.RefreshIntervalMinutes, 1, 1440);
            ValidateRange(s, result, McpSettingKeys.TimeoutSeconds, 1, 600);
            ValidatePatterns(s, result, McpSettingKeys.IncludeTools);
            ValidatePatterns(s, result, McpSettingKeys.ExcludeTools);
            ValidateSecrets(resource, result);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            McpResourceOptions options = McpResourceOptions.FromSettings(resource.Settings);
            TimeSpan limit = options.Timeout < Options.TestConnectionTimeout ? options.Timeout : Options.TestConnectionTimeout;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(limit);
                try
                {
                    using (McpRemoteSession session = await McpRemoteSession.ConnectAsync(options, resource, Options, timeout.Token).ConfigureAwait(false))
                    {
                        McpToolCatalog catalog = await session.GetCatalogAsync(true, options.RefreshInterval, Options.MaxListPages, limit, timeout.Token).ConfigureAwait(false);
                        Options.Catalogs.Put(McpToolBase.SessionKey(resource), catalog, Options.CatalogCacheCapacity);
                        int allowed = catalog.Tools.Count(t => options.Filter.IsAllowed(t.Name));
                        double ms = sw.Elapsed.TotalMilliseconds;
                        ConnectorHealthResult health = ConnectorHealthResult.Ok(
                            "Connected to " + session.Server.Display() + " at " + options.EndpointName() + "; "
                            + allowed.ToString(CultureInfo.InvariantCulture) + " of " + catalog.Tools.Count.ToString(CultureInfo.InvariantCulture) + " tools allowed.", ms);
                        health.Details["server"] = session.Server.Display();
                        health.Details["endpoint"] = options.EndpointName();
                        health.Details["transport"] = options.Transport.ToString();
                        health.Details["protocolVersion"] = session.Server.ProtocolVersion;
                        health.Details["tools"] = catalog.Tools.Count.ToString(CultureInfo.InvariantCulture);
                        health.Details["allowedTools"] = allowed.ToString(CultureInfo.InvariantCulture);
                        health.Details["resources"] = session.Server.HasResources ? "true" : "false";
                        health.Details["hasInstructions"] = String.IsNullOrWhiteSpace(session.Server.Instructions) ? "false" : "true";
                        return health;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "The MCP server did not respond within " + limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
                }
                catch (ConnectorException e)
                {
                    return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
                }
                catch (McpRemoteException e)
                {
                    ToolResult mapped = McpToolBase.MapRemoteError(e);
                    return ConnectorHealthResult.Fail(mapped.ErrorCode ?? ConnectorErrorCodes.UpstreamError, mapped.Text, sw.Elapsed.TotalMilliseconds);
                }
                catch (HttpRequestException e)
                {
                    ToolResult mapped = McpToolBase.MapHttpError(e, options.EndpointName());
                    return ConnectorHealthResult.Fail(mapped.ErrorCode ?? ConnectorErrorCodes.ConnectionFailed, mapped.Text, sw.Elapsed.TotalMilliseconds);
                }
                catch (InvalidOperationException e)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.ConnectionFailed, "The MCP session failed: " + TextClip.Clip(e.Message, 300), sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void ValidateSecrets(ConnectorResourceContext resource, ConnectorValidationResult result)
        {
            SecretAccessor secrets = resource.Secrets;
            bool hasAuth = false;
            if (secrets.HasSlot(McpSettingKeys.AuthorizationHeaderSlot))
            {
                string? token = secrets.Get(McpSettingKeys.AuthorizationHeaderSlot, McpSettingKeys.TokenField);
                if (String.IsNullOrWhiteSpace(token)) result.Add(McpSettingKeys.AuthorizationHeaderSlot, "required", "The secret bound to authorizationHeader needs a non-empty 'token' field.");
                else if (!McpHeaders.IsValidValue(token)) result.Add(McpSettingKeys.AuthorizationHeaderSlot, "invalid_format", "The authorizationHeader token contains line breaks or NUL characters.");
                else hasAuth = true;
            }

            if (!secrets.HasSlot(McpSettingKeys.ExtraHeadersSlot)) return;
            string? names = secrets.Get(McpSettingKeys.ExtraHeadersSlot, McpSettingKeys.ExtraHeaderNamesField);
            if (String.IsNullOrWhiteSpace(names))
            {
                result.Add(McpSettingKeys.ExtraHeadersSlot, "required", "The extraHeaders secret needs a 'names' field listing the header names, with one field per header holding its value.");
                return;
            }
            foreach (string name in names.Split(new char[] { ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!McpHeaders.IsValidName(name))
                {
                    result.Add(McpSettingKeys.ExtraHeadersSlot, "invalid_format", "'" + name + "' is not a valid header name.");
                    return;
                }
                if (McpHeaders.IsReserved(name))
                {
                    result.Add(McpSettingKeys.ExtraHeadersSlot, "invalid_value", "Header '" + name + "' is managed by the MCP transport and cannot be an extra header.");
                    return;
                }
                if (hasAuth && String.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(McpSettingKeys.ExtraHeadersSlot, "invalid_value", "Authorization is already set from the authorizationHeader slot; remove it from extraHeaders.");
                    return;
                }
                string? value = secrets.Get(McpSettingKeys.ExtraHeadersSlot, name);
                if (value == null || !McpHeaders.IsValidValue(value))
                {
                    result.Add(McpSettingKeys.ExtraHeadersSlot, "invalid_value", "Header '" + name + "' is listed in 'names' but has no valid value field.");
                    return;
                }
            }
        }

        private static void ValidatePatterns(ResourceSettings s, ConnectorValidationResult result, string key)
        {
            foreach (string pattern in s.GetStringList(key))
            {
                if (pattern.Any(c => Char.IsControl(c)))
                {
                    result.Add(key, "invalid_format", key + " entries must be tool names or * patterns without control characters.");
                    return;
                }
            }
        }

        private static void ValidateRange(ResourceSettings s, ConnectorValidationResult result, string key, long min, long max)
        {
            string? raw = s.GetString(key);
            if (raw == null) return;
            if (!Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < min || value > max)
                result.Add(key, "out_of_range", key + " must be an integer from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void ValidateBool(ResourceSettings s, ConnectorValidationResult result, string key)
        {
            string? raw = s.GetString(key);
            if (raw == null) return;
            string v = raw.Trim().ToLowerInvariant();
            if (v != "true" && v != "false" && v != "1" && v != "0" && v != "yes" && v != "no")
                result.Add(key, "invalid_value", key + " must be true or false.");
        }

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor(McpSettingKeys.Transport, SettingValueKindEnum.Enum, "Transport")
            {
                DefaultValue = "StreamableHttp",
                AllowedValues = new List<string> { "StreamableHttp", "Stdio" },
                Group = "Connection",
                ShowInSummary = true,
                Help = "StreamableHttp (default) reaches a remote server at url. Stdio starts command as a child process of the Hugo host and needs allowStdio here plus Connectors.Mcp.AllowStdio on the server."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.Url, SettingValueKindEnum.String, "Server URL")
            {
                Pattern = "^https?://",
                Group = "Connection",
                ShowInSummary = true,
                Help = "Absolute http or https URL of the Streamable HTTP endpoint including its path, for example https://mcp.example.com/mcp. Required for StreamableHttp. No credentials in the URL."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.Command, SettingValueKindEnum.String, "Command")
            {
                Group = "Connection",
                Help = "Stdio only: the executable that runs the MCP server, for example npx or /usr/local/bin/my-mcp-server."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.Arguments, SettingValueKindEnum.StringList, "Arguments")
            {
                Group = "Connection",
                Help = "Stdio only: command arguments, one per line. Passed without a shell."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.AllowStdio, SettingValueKindEnum.Boolean, "Allow Stdio")
            {
                DefaultValue = "false",
                Group = "Safety",
                Help = "Resource-level opt-in for the Stdio transport. Stdio also needs Connectors.Mcp.AllowStdio on the server, because the process runs inside the Hugo host with its privileges. Default false."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.IncludeTools, SettingValueKindEnum.StringList, "Include tools")
            {
                Group = "Safety",
                Help = "Remote tool names or * patterns (for example get_*) that agents may list and call, one per line. Empty allows every tool."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.ExcludeTools, SettingValueKindEnum.StringList, "Exclude tools")
            {
                Group = "Safety",
                Help = "Remote tool names or * patterns hidden from agents and refused, one per line. Wins over includeTools."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.RefreshIntervalMinutes, SettingValueKindEnum.Integer, "Tool list refresh (minutes)")
            {
                DefaultValue = "15",
                Minimum = 1,
                Maximum = 1440,
                Group = "Limits",
                Help = "How long a discovered tool list is reused before it is fetched again. Default 15, minimum 1, maximum 1440."
            });
            schema.Add(new SettingDescriptor(McpSettingKeys.TimeoutSeconds, SettingValueKindEnum.Integer, "Timeout (seconds)")
            {
                DefaultValue = "60",
                Minimum = 1,
                Maximum = 600,
                Group = "Limits",
                Help = "Longest time one remote call may take, including connecting. Default 60, minimum 1, maximum 600. The binding's timeout applies when shorter."
            });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = TypeName;
            d.Family = FamilyName;
            d.DisplayName = "External MCP server";
            d.Description = "Another MCP server attached as a resource: list its tools with their annotation-derived effects, call them with arguments passed through, and read its resources. Streamable HTTP, or Stdio when the server allows it.";
            d.Category = ConnectorCategoryEnum.Agent;
            d.IconKey = "plug";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(McpSettingKeys.AuthorizationHeaderSlot, "Authorization header", SecretKindEnum.BearerToken, SecretKindEnum.ApiToken) { Help = "BearerToken (or ApiToken) secret (field 'token') sent as Authorization: Bearer <token>. A value that already starts with a scheme, such as 'Basic dXNlcjpwYXNz', is sent as-is." },
                new SecretSlotDescriptor(McpSettingKeys.ExtraHeadersSlot, "Extra headers", SecretKindEnum.Generic) { Help = "Optional Generic secret: field 'names' lists header names (comma separated), and each listed name is a field holding that header's value, for example names=X-Api-Key with X-Api-Key=abc123." }
            };
            d.DefaultInstructionsKey = "connector." + TypeName + ".instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(McpConnector).Assembly, "connector.mcp.instructions.md");
            d.DocumentationUrl = "https://modelcontextprotocol.io/specification";
            return d;
        }

        #endregion
    }
}
