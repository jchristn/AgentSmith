namespace Hugo.Connectors.Agents.A2A
{
    /// <summary>
    /// Setting keys, secret slot names, and secret field names of the <c>a2a-agent</c> connector. These are part of the
    /// resource contract and are not configurable. Thread-safe.
    /// </summary>
    public static class A2AAgentSettingKeys
    {
        /// <summary>URL of the remote agent's card, or the agent's base URL (the well-known card path is appended).</summary>
        public const string AgentCardUrl = "agentCardUrl";

        /// <summary>Optional skill identifier from the card, sent with every delegated message as metadata.</summary>
        public const string SkillId = "skillId";

        /// <summary>Longest time one delegation may take, in seconds.</summary>
        public const string TimeoutSeconds = "timeoutSeconds";

        /// <summary>Whether delegations use SendStreamingMessage when the card advertises streaming.</summary>
        public const string Streaming = "streaming";

        /// <summary>Whether the card may point the endpoint (and the bearer token) at a different origin than agentCardUrl.</summary>
        public const string AllowCrossOriginEndpoint = "allowCrossOriginEndpoint";

        /// <summary>Secret slot whose token is sent as Authorization: Bearer.</summary>
        public const string BearerTokenSlot = "bearerToken";

        /// <summary>Secret field holding a token (ApiToken secrets).</summary>
        public const string TokenField = "token";
    }
}
