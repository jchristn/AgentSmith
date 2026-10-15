namespace Hugo.Connectors.Agents.A2A
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The settings of one <c>a2a-agent</c> resource, parsed with defaults and clamped to their ranges. Parsing never
    /// throws; problems are reported by <see cref="A2AAgentConnector.ValidateAsync"/> and, at call time, by
    /// <see cref="RequireCardUrl"/>. Not thread-safe for mutation; treat as immutable after <see cref="FromSettings"/>.
    /// </summary>
    public class A2AAgentResourceOptions
    {
        #region Public-Members

        /// <summary>
        /// Card URL (or agent base URL), or null when missing or invalid.
        /// </summary>
        public Uri? CardUrl { get; set; } = null;

        /// <summary>
        /// Skill identifier sent with delegated messages, or null.
        /// </summary>
        public string? SkillId { get; set; } = null;

        /// <summary>
        /// Longest time one delegation may take. Minimum 1 second, maximum 3600 seconds, default 120 seconds.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>
        /// Whether to stream when the card advertises streaming. Default false.
        /// </summary>
        public bool Streaming { get; set; } = false;

        /// <summary>
        /// Whether the card's endpoint may be on a different origin than the card URL. Default false.
        /// </summary>
        public bool AllowCrossOriginEndpoint { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static A2AAgentResourceOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            A2AAgentResourceOptions o = new A2AAgentResourceOptions();
            if (TryParseCardUrl(settings.GetString(A2AAgentSettingKeys.AgentCardUrl), out Uri? url, out string _)) o.CardUrl = url;
            string? skill = settings.GetString(A2AAgentSettingKeys.SkillId);
            o.SkillId = String.IsNullOrWhiteSpace(skill) ? null : skill.Trim();
            o.Timeout = TimeSpan.FromSeconds(settings.GetInt(A2AAgentSettingKeys.TimeoutSeconds, 120, 1, 3600));
            o.Streaming = settings.GetBool(A2AAgentSettingKeys.Streaming, false);
            o.AllowCrossOriginEndpoint = settings.GetBool(A2AAgentSettingKeys.AllowCrossOriginEndpoint, false);
            return o;
        }

        /// <summary>
        /// Parse a card URL: absolute http or https, no user info or fragment.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="url">Parsed URL.</param>
        /// <param name="error">Reason on failure.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParseCardUrl(string? value, out Uri? url, out string error)
        {
            url = null;
            error = String.Empty;
            if (String.IsNullOrWhiteSpace(value))
            {
                error = "agentCardUrl is required, for example https://agent.example.com/.well-known/agent-card.json.";
                return false;
            }
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                error = "agentCardUrl must be an absolute http or https URL, for example https://agent.example.com/.well-known/agent-card.json.";
                return false;
            }
            if (!String.IsNullOrEmpty(parsed.UserInfo))
            {
                error = "agentCardUrl must not contain credentials; bind a secret to the bearerToken slot instead.";
                return false;
            }
            if (!String.IsNullOrEmpty(parsed.Fragment))
            {
                error = "agentCardUrl must not contain a fragment.";
                return false;
            }
            url = parsed;
            return true;
        }

        /// <summary>
        /// The card URL, or an error when it is missing or invalid.
        /// </summary>
        /// <returns>URL.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when unusable.</exception>
        public Uri RequireCardUrl()
        {
            if (CardUrl == null) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The resource's agentCardUrl is missing or is not an absolute http or https URL without credentials.");
            return CardUrl;
        }

        #endregion
    }
}
