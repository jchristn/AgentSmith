namespace Hugo.Core.Knowledge
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Security;

    /// <summary>
    /// Atomically records reusable discoveries in Hugo, never in the target system. Thread-safe.
    /// </summary>
    public class ResourceUpdateContextTool : IConnectorTool
    {
        /// <inheritdoc />
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "resource_update_context",
            Title = "Update resource context",
            Description = "Record concise, confirmed reusable knowledge, not credentials, personal data, raw rows or one-off outputs. Changes Hugo only, never the target. scope defaults to context and key to _root. mode append (default) preserves existing content; replace is refused for operator-authored entries. Read first and supply expectedVersion (0 for new entries); version_conflict requires rereading before retry. Inferred facts must be labeled inferred.",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"scope\":{\"type\":\"string\",\"maxLength\":128},\"key\":{\"type\":\"string\",\"maxLength\":512},\"content\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":4000},\"mode\":{\"type\":\"string\",\"enum\":[\"append\",\"replace\"]},\"expectedVersion\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":2147483647}},\"required\":[\"content\"],\"additionalProperties\":false}",
            BaseEffect = EffectEnum.Read,
            UpdatesKnowledge = true,
            OutputDescription = "Stored context with scope, key, version, source, and writer; version_conflict means nothing changed."
        };

        /// <inheritdoc />
        public EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            return new EffectClassification(EffectEnum.Read, "Updates Hugo knowledge only; no target connection.", "Update resource context");
        }

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();
            if (!context.AllowKnowledgeUpdates) return ToolResult.Error("permission_denied", "This binding does not allow knowledge updates.");
            if (context.Resource.Knowledge is not IVersionedResourceKnowledgeStore store)
                return ToolResult.Error("not_supported", "This knowledge store does not support atomic versioned updates.");
            string scope = (arguments.GetString("scope") ?? "context").Trim();
            string key = arguments.GetString("key") ?? "_root";
            string mode = arguments.GetString("mode") ?? "append";
            string content = (arguments.GetString("content") ?? String.Empty).Trim();
            if (String.IsNullOrWhiteSpace(scope) || scope.Length > 128 || key.Length > 512 || (mode != "append" && mode != "replace"))
                return ToolResult.Error("invalid_argument", "Use a scope of 1 through 128 characters, key at most 512 characters, and mode append or replace.");
            if (content.Length == 0 || content.Length > 4000) return ToolResult.Error("invalid_argument", "content must be 1 through 4000 characters.");
            ResourceContextContentGuard guard = new ResourceContextContentGuard { MinSecretLength = 1 };
            string? refusal = guard.Check(content, context.Resource.Secrets.AllValues())
                ?? guard.Check(scope, context.Resource.Secrets.AllValues())
                ?? (key.Length == 0 ? null : guard.Check(key, context.Resource.Secrets.AllValues()));
            if (refusal != null) return ToolResult.Error("invalid_argument", refusal);
            key = key == "_root" ? String.Empty : key;
            ResourceKnowledgeEntry? previous = await store.GetAsync(scope, key, token).ConfigureAwait(false);
            int version = previous?.Version ?? 0;
            if (arguments.Has("expectedVersion") && arguments.GetLong("expectedVersion", -1) != version)
                return ToolResult.Error("version_conflict", "Context changed; read it again before updating.");
            if (previous?.Source == KnowledgeSourceEnum.Operator && mode == "replace")
                return ToolResult.Error("permission_denied", "An agent may append to operator-authored context but may not replace it.");
            string merged = mode == "append" && previous != null && previous.Content.Length > 0 ? previous.Content.TrimEnd() + "\n\n" + content : content;
            // Existing operator text is preserved verbatim; guard only the new contribution, while bounding the whole entry.
            if (merged.Length > guard.MaxEntryChars) return ToolResult.Error("too_large", "The resulting context is too long; ask the operator to curate it.");
            SecretScrubber scrubber = new SecretScrubber();
            scrubber.AddKnownValues(context.Resource.Secrets.AllValues());
            string clean = scrubber.Scrub(SecretScrubber.Shared.Scrub(merged)) ?? String.Empty;
            string actor = scrubber.Scrub(SecretScrubber.Shared.Scrub(context.PrincipalSummary)) ?? String.Empty;
            if (actor.Length > 256) actor = actor.Substring(0, 256);
            ResourceKnowledgeEntry? stored = await store.TryPutAsync(new ResourceKnowledgeEntry
            {
                Scope = scope,
                Key = key,
                Content = clean,
                Source = previous?.Source == KnowledgeSourceEnum.Operator ? KnowledgeSourceEnum.Operator : KnowledgeSourceEnum.Agent,
                UpdatedBy = "agent via resource_update_context: " + actor,
                Version = version
            }, version, token).ConfigureAwait(false);
            if (stored == null) return ToolResult.Error("version_conflict", "Context changed concurrently; read it again before updating.");
            int maxChars = Math.Min(12000, context.Limits.MaxBytes / 4);
            string text = scrubber.Scrub(ResourceContextFormatter.Format(new[] { stored }, maxChars)) ?? String.Empty;
            return ToolResult.FromText(text.Length > maxChars ? text.Substring(0, maxChars) : text);
        }
    }
}
