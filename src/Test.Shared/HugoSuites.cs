namespace Test.Shared
{
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Test.Shared.Connectors;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Registry of every Hugo test suite consumed by the console, xUnit, and NUnit runners.
    /// </summary>
    public static class HugoSuites
    {
        /// <summary>
        /// All suites.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
                foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
                {
                    suites.Add(DatabaseContractSuite.Build(provider));
                }
                suites.AddRange(TemplateSuite.Build());
                suites.AddRange(IdentitySuite.Build());
                suites.AddRange(FoundationSuite.Build());
                suites.AddRange(HelperPortSuite.Build());
                suites.AddRange(Test.Shared.Telemetry.LoggingSuite.Build());
                suites.AddRange(AuthorizationMatrixSuite.Build());
                suites.AddRange(TenantScopingSuite.Build());
                suites.AddRange(SecretsSuite.Build());
                suites.AddRange(Test.Shared.Agent.AgentSuites.Build());
                suites.AddRange(Test.Shared.Suites.ConnectorFrameworkSuites.Build());
                suites.AddRange(AbstractionsSuites.Build());
                suites.AddRange(SshSuites.Build());
                suites.AddRange(StorageSuites.Build());
                suites.AddRange(SqlSuites.Build());
                suites.AddRange(LoggingSuites.Build());
                suites.AddRange(AwsSuites.Build());
                suites.AddRange(RedisSuites.Build());
                suites.AddRange(HttpSuites.Build());
                suites.AddRange(ObservabilitySuites.Build());
                suites.AddRange(KubernetesSuites.Build());
                suites.AddRange(DockerSuites.Build());
                suites.AddRange(Test.Shared.Integration.PipelineSuite.Build());
                suites.AddRange(Test.Shared.Integration.ConfigurationIntegrationSuite.Build());
                suites.AddRange(Test.Shared.Integration.ModelAgentSuite.Build());
                suites.AddRange(Test.Shared.Integration.IntegrationUnitSuite.Build());
                suites.AddRange(Test.Shared.Integration.ResourceHealthSuite.Build());
                suites.AddRange(Test.Shared.Integration.ResourceContextSuite.Build());
                suites.AddRange(Test.Shared.Integration.IntegrationDataSuite.Build());
                suites.AddRange(Test.Shared.Integration.FollowUpSuite.Build());
                suites.AddRange(Test.Shared.Integration.ThirdPartyInstructionsSuite.Build());
                suites.AddRange(Test.Shared.Integration.SnapshotPluginSuite.Build());
                suites.AddRange(Test.Shared.Chat.ChatSuite.Build());
                suites.AddRange(Test.Shared.Chat.A2ASuite.Build());
                suites.AddRange(Test.Shared.Chat.OpenAISuite.Build());
                suites.AddRange(AgentsSuites.Build());
                suites.AddRange(Test.Shared.Cli.CliSuites.Build());
                suites.AddRange(Test.Shared.Integration.Mcp.McpSuite.Build());
                return suites;
            }
        }
    }
}
