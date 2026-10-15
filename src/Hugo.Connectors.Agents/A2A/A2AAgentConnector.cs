namespace Hugo.Connectors.Agents.A2A
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.A2A.Tools;
    using Voltaic.A2A;

    /// <summary>
    /// Remote A2A agent connector (type <c>a2a-agent</c>, family <c>a2a</c>). One resource is one remote agent, found
    /// through its agent card and reached over A2A 1.0 JSON-RPC (or HTTP+JSON). Tools: <c>a2a_get_card</c> (Read),
    /// <c>a2a_delegate</c> (Write), and <c>a2a_get_task</c> (Read). Thread-safe.
    /// </summary>
    public class A2AAgentConnector : IConnector, IRemoteInstructionsProvider
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "a2a-agent";

        /// <summary>
        /// Tool family.
        /// </summary>
        public static readonly string FamilyName = "a2a";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Options shared with the tools. Never null.
        /// </summary>
        public A2AAgentConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with default options.
        /// </summary>
        public A2AAgentConnector() : this(new A2AAgentConnectorOptions())
        {
        }

        /// <summary>
        /// Instantiate with options.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public A2AAgentConnector(A2AAgentConnectorOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new A2AGetCardTool(Options),
                new A2ADelegateTool(Options),
                new A2AGetTaskTool(Options)
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

            if (!A2AAgentResourceOptions.TryParseCardUrl(s.GetString(A2AAgentSettingKeys.AgentCardUrl), out Uri? _, out string urlError))
                result.Add(A2AAgentSettingKeys.AgentCardUrl, String.IsNullOrWhiteSpace(s.GetString(A2AAgentSettingKeys.AgentCardUrl)) ? "required" : "invalid_format", urlError);

            string? skill = s.GetString(A2AAgentSettingKeys.SkillId);
            if (skill != null && skill.Trim().Any(c => Char.IsWhiteSpace(c) || Char.IsControl(c)))
                result.Add(A2AAgentSettingKeys.SkillId, "invalid_format", "skillId must be a single skill identifier from the agent card, without spaces.");

            string? raw = s.GetString(A2AAgentSettingKeys.TimeoutSeconds);
            if (raw != null && (!Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds) || seconds < 1 || seconds > 3600))
                result.Add(A2AAgentSettingKeys.TimeoutSeconds, "out_of_range", "timeoutSeconds must be an integer from 1 to 3600.");

            ValidateBool(s, result, A2AAgentSettingKeys.Streaming);
            ValidateBool(s, result, A2AAgentSettingKeys.AllowCrossOriginEndpoint);

            if (resource.Secrets.HasSlot(A2AAgentSettingKeys.BearerTokenSlot))
            {
                string? bearer = resource.Secrets.Get(A2AAgentSettingKeys.BearerTokenSlot, A2AAgentSettingKeys.TokenField);
                if (String.IsNullOrWhiteSpace(bearer)) result.Add(A2AAgentSettingKeys.BearerTokenSlot, "required", "The secret bound to bearerToken needs a non-empty 'token' field.");
                else if (bearer.Any(c => c == '\r' || c == '\n' || c == '\0')) result.Add(A2AAgentSettingKeys.BearerTokenSlot, "invalid_format", "The bearerToken contains line breaks or NUL characters.");
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            A2AAgentResourceOptions options = A2AAgentResourceOptions.FromSettings(resource.Settings);
            TimeSpan limit = options.Timeout < Options.TestConnectionTimeout ? options.Timeout : Options.TestConnectionTimeout;
            string origin = options.CardUrl?.GetLeftPart(UriPartial.Authority) ?? "(no agentCardUrl)";
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(limit);
                try
                {
                    using (A2ARemoteSession session = await A2ARemoteSession.ConnectAsync(options, resource, timeout.Token).ConfigureAwait(false))
                    {
                        double ms = sw.Elapsed.TotalMilliseconds;
                        AgentCard card = session.Card;
                        int skills = card.Skills?.Count ?? 0;
                        string name = TextClip.OneLine(card.Name);
                        ConnectorHealthResult health;
                        if (options.SkillId != null && !(card.Skills ?? new List<AgentSkill>()).Any(sk => sk != null && String.Equals(sk.Id, options.SkillId, StringComparison.Ordinal)))
                            health = ConnectorHealthResult.Fail(ConnectorErrorCodes.InvalidSetting, "The agent card of " + name + " has no skill '" + options.SkillId + "'. Pick one of: " + String.Join(", ", (card.Skills ?? new List<AgentSkill>()).Select(sk => sk.Id)) + ".", ms);
                        else
                            health = ConnectorHealthResult.Ok("Resolved the agent card of " + name + " (" + skills.ToString(CultureInfo.InvariantCulture) + " skills), reachable over " + session.Binding + " at " + session.EndpointUrl + ".", ms);
                        health.Details["agent"] = name;
                        health.Details["version"] = card.Version ?? String.Empty;
                        health.Details["endpoint"] = session.EndpointUrl;
                        health.Details["binding"] = session.Binding;
                        health.Details["skills"] = skills.ToString(CultureInfo.InvariantCulture);
                        health.Details["streaming"] = session.SupportsStreaming ? "true" : "false";
                        return health;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "The agent card did not load within " + limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
                }
                catch (ConnectorException e)
                {
                    return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
                }
                catch (HttpRequestException e)
                {
                    ToolResult mapped = A2AToolBase.MapHttpError(e, origin);
                    return ConnectorHealthResult.Fail(mapped.ErrorCode ?? ConnectorErrorCodes.ConnectionFailed, mapped.Text, sw.Elapsed.TotalMilliseconds);
                }
                catch (A2AProtocolException e)
                {
                    ToolResult mapped = A2AToolBase.MapProtocolError(e);
                    return ConnectorHealthResult.Fail(mapped.ErrorCode ?? ConnectorErrorCodes.UpstreamError, mapped.Text, sw.Elapsed.TotalMilliseconds);
                }
                catch (JsonException e)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.UpstreamError, "The agent card could not be read: " + TextClip.Clip(e.Message, 300), sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        /// <summary>
        /// The remote agent's own description of itself from its agent card: the card description and its skills,
        /// clipped to <see cref="A2AAgentConnectorOptions.MaxTextChars"/>. Hugo fences the text as third-party content.
        /// </summary>
        /// <param name="resource">Resource context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The card text, or null when the card has no description and no skills.</returns>
        /// <exception cref="ConnectorException">Thrown when the agent card cannot be loaded.</exception>
        public async Task<string?> GetRemoteInstructionsAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            A2AAgentResourceOptions options = A2AAgentResourceOptions.FromSettings(resource.Settings);
            string origin = options.CardUrl?.GetLeftPart(UriPartial.Authority) ?? "(no agentCardUrl)";
            try
            {
                using (A2ARemoteSession session = await A2ARemoteSession.ConnectAsync(options, resource, token).ConfigureAwait(false))
                {
                    AgentCard card = session.Card;
                    StringBuilder sb = new StringBuilder();
                    if (!String.IsNullOrWhiteSpace(card.Description)) sb.Append(card.Description.Trim()).Append('\n');
                    List<AgentSkill> skills = (card.Skills ?? new List<AgentSkill>()).Where(sk => sk != null).ToList();
                    if (skills.Count > 0)
                    {
                        sb.Append("Skills:\n");
                        foreach (AgentSkill skill in skills)
                        {
                            sb.Append("- ").Append(TextClip.OneLine(skill.Id));
                            if (!String.IsNullOrWhiteSpace(skill.Name)) sb.Append(" (").Append(TextClip.OneLine(skill.Name)).Append(')');
                            if (!String.IsNullOrWhiteSpace(skill.Description)) sb.Append(": ").Append(TextClip.OneLine(skill.Description));
                            sb.Append('\n');
                        }
                    }
                    return sb.Length == 0 ? null : TextClip.Clip(sb.ToString(), Options.MaxTextChars);
                }
            }
            catch (HttpRequestException e)
            {
                ToolResult mapped = A2AToolBase.MapHttpError(e, origin);
                throw new ConnectorException(mapped.ErrorCode ?? ConnectorErrorCodes.ConnectionFailed, mapped.Text);
            }
            catch (A2AProtocolException e)
            {
                ToolResult mapped = A2AToolBase.MapProtocolError(e);
                throw new ConnectorException(mapped.ErrorCode ?? ConnectorErrorCodes.UpstreamError, mapped.Text);
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The agent card could not be read: " + TextClip.Clip(e.Message, 300));
            }
        }

        #endregion

        #region Private-Methods

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
            schema.Add(new SettingDescriptor(A2AAgentSettingKeys.AgentCardUrl, SettingValueKindEnum.String, "Agent card URL")
            {
                Required = true,
                Pattern = "^https?://",
                Group = "Connection",
                ShowInSummary = true,
                Help = "URL of the remote agent's card, for example https://agent.example.com/.well-known/agent-card.json, or the agent's base URL (the well-known path is added). No credentials in the URL."
            });
            schema.Add(new SettingDescriptor(A2AAgentSettingKeys.SkillId, SettingValueKindEnum.String, "Skill")
            {
                Group = "Connection",
                ShowInSummary = true,
                Help = "Optional skill identifier from the card. It is sent as skillId metadata with every delegated message, and Test Connection checks that the card lists it."
            });
            schema.Add(new SettingDescriptor(A2AAgentSettingKeys.TimeoutSeconds, SettingValueKindEnum.Integer, "Timeout (seconds)")
            {
                DefaultValue = "120",
                Minimum = 1,
                Maximum = 3600,
                Group = "Limits",
                Help = "Longest time a2a_delegate waits for the remote task to complete or ask for input. Default 120, minimum 1, maximum 3600. The binding's timeout applies when shorter."
            });
            schema.Add(new SettingDescriptor(A2AAgentSettingKeys.Streaming, SettingValueKindEnum.Boolean, "Use streaming")
            {
                DefaultValue = "false",
                Group = "Connection",
                Help = "Use SendStreamingMessage when the card advertises streaming, so progress and artifacts arrive as they happen. Otherwise Hugo sends a blocking request and polls the task. Default false."
            });
            schema.Add(new SettingDescriptor(A2AAgentSettingKeys.AllowCrossOriginEndpoint, SettingValueKindEnum.Boolean, "Allow endpoint on another origin")
            {
                DefaultValue = "false",
                Group = "Safety",
                Help = "When false (default), the endpoint named in the card must be on the same scheme, host, and port as agentCardUrl, so a card cannot redirect the bearer token elsewhere."
            });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = TypeName;
            d.Family = FamilyName;
            d.DisplayName = "Remote A2A agent";
            d.Description = "Another agent reached over the A2A protocol: read its card and skills, delegate tasks and wait for the result or for its request for more input, and check on tasks later.";
            d.Category = ConnectorCategoryEnum.Agent;
            d.IconKey = "bot";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(A2AAgentSettingKeys.BearerTokenSlot, "Bearer token", SecretKindEnum.ApiToken) { Help = "ApiToken secret (field 'token') sent as Authorization: Bearer <token> to the card URL and the agent endpoint. A value that already starts with a scheme is sent as-is." }
            };
            d.DefaultInstructionsKey = "connector." + TypeName + ".instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(A2AAgentConnector).Assembly, "connector.a2a-agent.instructions.md");
            d.DocumentationUrl = "https://a2a-protocol.org/latest/specification/";
            return d;
        }

        #endregion
    }
}
