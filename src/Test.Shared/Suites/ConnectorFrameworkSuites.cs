namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Logging;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enums;
    using Hugo.Core.Safety;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the connector framework pieces in Hugo.Core: access policy matrix, settings validation, registry.
    /// </summary>
    public static class ConnectorFrameworkSuites
    {
        private const string PolicySuite = "framework.policy";
        private const string ValidatorSuite = "framework.settings";
        private const string RegistrySuite = "framework.registry";

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(PolicySuite, "Access policy matrix", PolicyCases()),
                new TestSuiteDescriptor(ValidatorSuite, "Connector settings validation", ValidatorCases()),
                new TestSuiteDescriptor(RegistrySuite, "Connector registry", RegistryCases())
            };
        }

        private static List<TestCaseDescriptor> PolicyCases()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(PolicySuite, "matrix", "Effect by access mode matrix (plan 11.2)", ct =>
            {
                Expect(EffectEnum.Read, AccessModeEnum.ReadOnly, true, InvocationDecisionEnum.Allowed);
                Expect(EffectEnum.Read, AccessModeEnum.ReadWriteWithApproval, true, InvocationDecisionEnum.Allowed);
                Expect(EffectEnum.Read, AccessModeEnum.ReadWrite, true, InvocationDecisionEnum.Allowed);
                Expect(EffectEnum.Write, AccessModeEnum.ReadOnly, true, InvocationDecisionEnum.Denied);
                Expect(EffectEnum.Write, AccessModeEnum.ReadWriteWithApproval, true, InvocationDecisionEnum.ApprovalRequired);
                Expect(EffectEnum.Write, AccessModeEnum.ReadWrite, true, InvocationDecisionEnum.Allowed);
                Expect(EffectEnum.Destructive, AccessModeEnum.ReadOnly, true, InvocationDecisionEnum.Denied);
                Expect(EffectEnum.Destructive, AccessModeEnum.ReadWriteWithApproval, true, InvocationDecisionEnum.ApprovalRequired);
                Expect(EffectEnum.Destructive, AccessModeEnum.ReadWrite, true, InvocationDecisionEnum.ApprovalRequired);
                Expect(EffectEnum.Destructive, AccessModeEnum.ReadWrite, false, InvocationDecisionEnum.Allowed);
                Expect(EffectEnum.Destructive, AccessModeEnum.ReadWriteWithApproval, false, InvocationDecisionEnum.ApprovalRequired);
                Expect(EffectEnum.Destructive, AccessModeEnum.ReadOnly, false, InvocationDecisionEnum.Denied);
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor(PolicySuite, "rules-and-ceiling", "Tool rules, ceiling, lockdown, rejection, always-approve", ct =>
            {
                AccessPolicyInput input = Input(EffectEnum.Write, AccessModeEnum.ReadWrite, true);
                input.ResourceCeiling = AccessModeEnum.ReadOnly;
                Check(AccessPolicy.Decide(input).Decision == InvocationDecisionEnum.Denied, "ceiling caps ReadWrite to ReadOnly");

                input = Input(EffectEnum.Read, AccessModeEnum.ReadWrite, true);
                input.ToolRule = ToolRuleEnum.Deny;
                Check(AccessPolicy.Decide(input).ReasonCode == "tool_denied", "deny rule wins over Read");

                input = Input(EffectEnum.Read, AccessModeEnum.ReadOnly, true);
                input.ToolRule = ToolRuleEnum.RequireApproval;
                Check(AccessPolicy.Decide(input).Decision == InvocationDecisionEnum.ApprovalRequired, "require-approval upgrades Read");

                input = Input(EffectEnum.Write, AccessModeEnum.ReadOnly, true);
                input.ToolRule = ToolRuleEnum.Allow;
                Check(AccessPolicy.Decide(input).Decision == InvocationDecisionEnum.Denied, "allow rule cannot override ReadOnly");

                input = Input(EffectEnum.Write, AccessModeEnum.ReadWrite, true);
                input.TenantLockdown = true;
                Check(AccessPolicy.Decide(input).ReasonCode == "tenant_lockdown", "lockdown denies writes");
                input = Input(EffectEnum.Read, AccessModeEnum.ReadWrite, true);
                input.TenantLockdown = true;
                Check(AccessPolicy.Decide(input).Decision == InvocationDecisionEnum.Allowed, "lockdown allows reads");

                input = Input(EffectEnum.Read, AccessModeEnum.ReadWrite, true);
                input.Classification = EffectClassification.Reject("matches deny pattern");
                Check(AccessPolicy.Decide(input).ReasonCode == "classifier_rejected", "classifier rejection");

                input = Input(EffectEnum.Destructive, AccessModeEnum.ReadWrite, false);
                input.Classification.AlwaysRequireApproval = true;
                Check(AccessPolicy.Decide(input).Decision == InvocationDecisionEnum.ApprovalRequired, "always-approve beats opt-out");
                return Task.CompletedTask;
            }));
            return cases;
        }

        private static List<TestCaseDescriptor> ValidatorCases()
        {
            ConnectorDescriptor d = new ConnectorDescriptor { Type = "sample" };
            d.SettingsSchema
                .Add(new SettingDescriptor("host", SettingValueKindEnum.String, "Host") { Required = true, Pattern = "^[a-z0-9.-]+$" })
                .Add(new SettingDescriptor("port", SettingValueKindEnum.Integer, "Port") { DefaultValue = "22", Minimum = 1, Maximum = 65535 })
                .Add(new SettingDescriptor("sudo", SettingValueKindEnum.Boolean, "Sudo") { DefaultValue = "false" })
                .Add(new SettingDescriptor("mode", SettingValueKindEnum.Enum, "Mode") { AllowedValues = new List<string> { "Password", "PrivateKey" } });
            d.SecretSlots.Add(new SecretSlotDescriptor("password", "Password", SecretKindEnum.Password) { Required = true });

            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(ValidatorSuite, "valid", "Valid settings pass and defaults apply", ct =>
            {
                Dictionary<string, string> values = new Dictionary<string, string> { { "host", "web-01.example.com" }, { "mode", "Password" } };
                ConnectorValidationResult r = ConnectorSettingsValidator.Validate(d, values, new Dictionary<string, SecretKindEnum> { { "password", SecretKindEnum.Password } });
                Check(r.IsValid, "valid");
                Dictionary<string, string> withDefaults = ConnectorSettingsValidator.ApplyDefaults(d, values);
                Check(withDefaults["port"] == "22" && withDefaults["sudo"] == "false", "defaults");
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor(ValidatorSuite, "errors", "Each kind of problem produces a field error", ct =>
            {
                Dictionary<string, string> values = new Dictionary<string, string> { { "host", "BAD HOST" }, { "port", "70000" }, { "sudo", "maybe" }, { "mode", "Kerberos" }, { "extra", "x" } };
                ConnectorValidationResult r = ConnectorSettingsValidator.Validate(d, values, new Dictionary<string, SecretKindEnum> { { "token", SecretKindEnum.ApiToken } });
                HashSet<string> codes = new HashSet<string>();
                foreach (ConnectorValidationError e in r.Errors) codes.Add(e.Field + ":" + e.Code);
                Check(codes.Contains("host:invalid_format"), "pattern");
                Check(codes.Contains("port:out_of_range"), "range");
                Check(codes.Contains("sudo:invalid_format"), "bool");
                Check(codes.Contains("mode:not_allowed"), "enum");
                Check(codes.Contains("extra:unknown_setting"), "unknown setting");
                Check(codes.Contains("token:unknown_slot"), "unknown slot");
                Check(codes.Contains("password:required"), "required slot");
                r = ConnectorSettingsValidator.Validate(d, new Dictionary<string, string>(), new Dictionary<string, SecretKindEnum> { { "password", SecretKindEnum.PrivateKey } });
                codes.Clear();
                foreach (ConnectorValidationError e in r.Errors) codes.Add(e.Field + ":" + e.Code);
                Check(codes.Contains("host:required"), "required setting");
                Check(codes.Contains("password:kind_not_accepted"), "slot kind");
                return Task.CompletedTask;
            }));
            return cases;
        }

        private static List<TestCaseDescriptor> RegistryCases()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(RegistrySuite, "logs-family-parity", "Papertrail and SolarWinds share the logs family with identical shared schemas", ct =>
            {
                using (ConnectorRegistry registry = new ConnectorRegistry())
                {
                    registry.Register(new PapertrailConnector());
                    registry.Register(new SolarWindsObservabilityConnector());
                    List<FamilyTool> tools = registry.FamilyTools("logs");
                    FamilyTool? search = tools.Find(t => t.Descriptor.Name == "logs_search");
                    Check(search != null && search.ConnectorTypes.Count == 2, "logs_search offered by both");
                    Check(registry.FindTool("papertrail", "logs_list_systems") != null, "find tool");
                    bool threw = false;
                    try { registry.Register(new PapertrailConnector()); } catch (InvalidOperationException) { threw = true; }
                    Check(threw, "duplicate type rejected");
                }
                return Task.CompletedTask;
            }));
            return cases;
        }

        private static AccessPolicyInput Input(EffectEnum effect, AccessModeEnum mode, bool requireDestructive)
        {
            return new AccessPolicyInput
            {
                Classification = new EffectClassification(effect, "test", "test"),
                AccessMode = mode,
                RequireApprovalForDestructive = requireDestructive
            };
        }

        private static void Expect(EffectEnum effect, AccessModeEnum mode, bool requireDestructive, InvocationDecisionEnum expected)
        {
            AccessPolicyDecision d = AccessPolicy.Decide(Input(effect, mode, requireDestructive));
            Check(d.Decision == expected, effect + " under " + mode + " (destructive approval " + requireDestructive + ") expected " + expected + " got " + d.Decision);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }
    }
}
