namespace Test.Xunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;
    using global::Xunit;

    /// <summary>
    /// Runs every Hugo suite through xUnit in one fact, preserving suite order and lifecycle hooks.
    /// </summary>
    public sealed class HugoFactTests : TouchstoneFactBase
    {
        /// <summary>
        /// Suites to run.
        /// </summary>
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get
            {
                return HugoSuites.All;
            }
        }

        /// <summary>
        /// Run all suites.
        /// </summary>
        /// <returns>Task.</returns>
        [Fact]
        public async Task RunAll()
        {
            await RunAllAsync();
        }
    }
}
