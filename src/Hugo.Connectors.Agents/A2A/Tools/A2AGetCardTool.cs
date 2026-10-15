namespace Hugo.Connectors.Agents.A2A.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.A2A.Payloads;
    using Voltaic.A2A;

    /// <summary>
    /// <c>a2a_get_card</c>: summarize the remote agent's card (who it is, what it can do, its skills, and how Hugo reaches
    /// it). Always Read. The card is cached with the session and refreshed after the connector's card refresh interval
    /// or on demand. Thread-safe.
    /// </summary>
    public class A2AGetCardTool : A2AToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public static readonly string ToolName = "a2a_get_card";

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Fetch the card again instead of using the cached copy. Default false.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public A2AGetCardTool(A2AAgentConnectorOptions options) : base(BuildDescriptor(), options)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the card payload.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="resource">Parsed resource settings.</param>
        /// <param name="maxSkills">Most skills included.</param>
        /// <param name="maxChars">Most characters per description.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static A2ACardPayload BuildPayload(A2ARemoteSession session, A2AAgentResourceOptions resource, int maxSkills, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(resource);
            AgentCard card = session.Card;
            List<AgentSkill> skills = (card.Skills ?? new List<AgentSkill>()).Where(s => s != null).ToList();
            A2ACardPayload p = new A2ACardPayload
            {
                Name = TextClip.OneLine(UntrustedContent.Neutralize(card.Name)),
                Description = TextClip.Clip(UntrustedContent.Neutralize(card.Description), maxChars),
                Version = String.IsNullOrWhiteSpace(card.Version) ? null : TextClip.OneLine(card.Version),
                Provider = card.Provider?.Organization,
                DocumentationUrl = card.DocumentationUrl,
                CardUrl = resource.CardUrl?.AbsoluteUri ?? String.Empty,
                Endpoint = session.EndpointUrl,
                Binding = session.Binding,
                Streaming = session.SupportsStreaming,
                StreamingUsed = resource.Streaming && session.SupportsStreaming,
                DefaultInputModes = card.DefaultInputModes ?? new List<string>(),
                DefaultOutputModes = card.DefaultOutputModes ?? new List<string>(),
                ConfiguredSkillId = resource.SkillId,
                ConfiguredSkillFound = resource.SkillId == null ? null : skills.Any(s => String.Equals(s.Id, resource.SkillId, StringComparison.Ordinal)),
                SkillCount = skills.Count,
                FetchedUtc = session.CardFetchedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            };
            foreach (AgentSkill s in skills.Take(Math.Max(0, maxSkills)))
            {
                p.Skills.Add(new A2ASkillPayload
                {
                    Id = s.Id ?? String.Empty,
                    Name = TextClip.OneLine(UntrustedContent.Neutralize(s.Name)),
                    Description = TextClip.Clip(UntrustedContent.Neutralize(s.Description), Math.Min(maxChars, 2000)),
                    Tags = s.Tags ?? new List<string>(),
                    Examples = (s.Examples ?? new List<string>()).Take(5).Select(e => TextClip.Clip(UntrustedContent.Neutralize(e), 300)).ToList()
                });
            }
            return p;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, A2AAgentResourceOptions resource, A2ARemoteSession session, ToolArguments arguments, TimeSpan limit, CancellationToken callerToken, CancellationToken token)
        {
            if (arguments.GetBool("refresh", false) || DateTime.UtcNow - session.CardFetchedUtc > Options.CardRefreshInterval)
                await session.RefreshCardAsync(token).ConfigureAwait(false);

            A2ACardPayload p = BuildPayload(session, resource, context.Limits.MaxRows, Options.MaxTextChars);
            StringBuilder card = new StringBuilder();
            card.Append("Name: ").Append(p.Name).Append(p.Version == null ? String.Empty : " " + p.Version).Append('\n');
            if (p.Provider != null) card.Append("Provider: ").Append(TextClip.OneLine(UntrustedContent.Neutralize(p.Provider))).Append('\n');
            if (p.Description.Length > 0) card.Append("Description: ").Append(p.Description).Append('\n');
            if (p.DefaultInputModes.Any()) card.Append("Input modes: ").Append(String.Join(", ", p.DefaultInputModes)).Append('\n');
            if (p.DefaultOutputModes.Any()) card.Append("Output modes: ").Append(String.Join(", ", p.DefaultOutputModes)).Append('\n');
            card.Append("Skills (").Append(p.SkillCount.ToString(CultureInfo.InvariantCulture)).Append(p.Skills.Count < p.SkillCount ? ", first " + p.Skills.Count.ToString(CultureInfo.InvariantCulture) + " shown" : String.Empty).Append("):\n");
            foreach (A2ASkillPayload s in p.Skills)
            {
                card.Append("- ").Append(s.Id).Append(": ").Append(s.Name).Append('\n');
                if (s.Description.Length > 0) card.Append("  ").Append(TextClip.OneLine(s.Description)).Append('\n');
                if (s.Tags.Any()) card.Append("  tags: ").Append(String.Join(", ", s.Tags.Select(t => TextClip.OneLine(UntrustedContent.Neutralize(t))))).Append('\n');
                foreach (string e in s.Examples) card.Append("  example: ").Append(TextClip.OneLine(e)).Append('\n');
            }
            if (!p.Skills.Any()) card.Append("(the card lists no skills)\n");

            StringBuilder sb = new StringBuilder();
            sb.Append("Remote A2A agent card from ").Append(p.CardUrl).Append('\n');
            sb.Append("Hugo reaches it at ").Append(p.Endpoint).Append(" (").Append(p.Binding).Append(p.StreamingUsed ? ", streaming" : String.Empty).Append(").\n");
            if (p.ConfiguredSkillId != null)
                sb.Append("Configured skill: ").Append(p.ConfiguredSkillId).Append(p.ConfiguredSkillFound == true ? " (on the card)" : " (NOT on the card)").Append('\n');
            sb.Append(UntrustedContent.Fence("agent card of " + p.Name, card.ToString()));
            sb.Append("Delegate a task with a2a_delegate {\"message\": \"...\"}; continue a conversation with contextId, or answer an input-required task with taskId and contextId.\n");

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.StructuredJson = AgentsJson.Write(p);
            result.Metrics["skills"] = p.SkillCount;
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = ToolName,
                Title = "Get remote agent card",
                Description =
                    "Show the remote A2A agent's card: name, description, provider, input and output modes, and skills with examples, plus the endpoint Hugo uses. "
                    + "Call this first to learn what the agent claims it can do, then delegate only tasks that fit its skills with a2a_delegate. "
                    + "The card is the remote agent's own description (third-party claims).",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text summary plus structured JSON {name, description, version, provider, documentationUrl, cardUrl, endpoint, binding, streaming, streamingUsed, defaultInputModes, defaultOutputModes, configuredSkillId, configuredSkillFound, skillCount, skills[{id, name, description, tags, examples}], fetchedUtc}."
            };
        }

        #endregion
    }
}
