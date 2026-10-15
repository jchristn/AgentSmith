namespace Test.Shared.Connectors.Echo
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The test echo connector (P3.20, plan section 8.5 worked example): type echo, family echo, tools echo_read (R),
    /// echo_write (W), echo_delete (D), echo_classify (C), echo_knowledge (UpdatesKnowledge), echo_model
    /// (RequiresModel), and echo_learn (LearnSetting). Settings: greeting, hostKeyFingerprint, mode. Secret slot token
    /// (ApiToken, optional). Used by the pipeline, approval, knowledge, model, and agent runtime suites. The optional
    /// remoteInstructions setting plays a third party's own guidance (<see cref="IRemoteInstructionsProvider"/>).
    /// </summary>
    public class EchoConnector : IConnector, IRemoteInstructionsProvider
    {
        #region Public-Members

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="type">Connector type. Default echo.</param>
        /// <param name="family">Family. Default echo.</param>
        public EchoConnector(string type = "echo", string family = "echo")
        {
            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = type;
            d.Family = family;
            d.DisplayName = "Echo (" + type + ")";
            d.Description = "Test connector that echoes its arguments.";
            d.Category = ConnectorCategoryEnum.Other;
            d.DefaultInstructions = "Echo resources repeat what you send. Use echo_read for reads.";
            d.SettingsSchema.Add(new SettingDescriptor("greeting", SettingValueKindEnum.String, "Greeting") { DefaultValue = "hello", ShowInSummary = true });
            d.SettingsSchema.Add(new SettingDescriptor("hostKeyFingerprint", SettingValueKindEnum.String, "Pinned host key"));
            d.SettingsSchema.Add(new SettingDescriptor("mode", SettingValueKindEnum.Enum, "Mode") { AllowedValues = new List<string> { "normal", "invalid", "hostKeyMismatch" }, DefaultValue = "normal" });
            d.SettingsSchema.Add(new SettingDescriptor("remoteInstructions", SettingValueKindEnum.String, "Remote instructions (plays a third party's own guidance)"));
            d.SecretSlots.Add(new SecretSlotDescriptor("token", "Token", SecretKindEnum.ApiToken, SecretKindEnum.Generic));
            Descriptor = d;
            string prefix = family + "_";
            Tools = new List<IConnectorTool>
            {
                new EchoTool(prefix + "read", EffectEnum.Read, EchoToolKindEnum.Echo),
                new EchoTool(prefix + "write", EffectEnum.Write, EchoToolKindEnum.Echo),
                new EchoTool(prefix + "delete", EffectEnum.Destructive, EchoToolKindEnum.Echo),
                new EchoTool(prefix + "classify", EffectEnum.Read, EchoToolKindEnum.Classify),
                new EchoTool(prefix + "knowledge", EffectEnum.Read, EchoToolKindEnum.Knowledge),
                new EchoTool(prefix + "model", EffectEnum.Read, EchoToolKindEnum.Model),
                new EchoTool(prefix + "learn", EffectEnum.Read, EchoToolKindEnum.Learn)
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ConnectorValidationResult result = new ConnectorValidationResult();
            if (resource.Settings.GetString("mode") == "invalid") result.Add("mode", "invalid_setting", "Mode invalid is rejected by the connector.");
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            string? greeting = resource.Settings.GetString("greeting");
            if (resource.Settings.GetString("mode") == "hostKeyMismatch") return ConnectorHealthResult.Fail(ConnectorErrorCodes.HostKeyMismatch, "The host presented SHA256:other, not the pinned SHA256:echo-pinned.", 1);
            if (greeting == "slow") await Task.Delay(TimeSpan.FromMinutes(5), token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(resource.Settings.GetString("hostKeyFingerprint"))) resource.LearnSetting?.Invoke("hostKeyFingerprint", "SHA256:echo-pinned");
            string? secret = resource.Secrets.Get("token", "token");
            ConnectorHealthResult ok = ConnectorHealthResult.Ok("echo ok " + greeting + (secret == null ? String.Empty : " token " + secret), 1);
            return ok;
        }

        /// <inheritdoc />
        public Task<string?> GetRemoteInstructionsAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            string? text = resource.Settings.GetString("remoteInstructions");
            return Task.FromResult(String.IsNullOrEmpty(text) ? null : text);
        }

        #endregion
    }
}
