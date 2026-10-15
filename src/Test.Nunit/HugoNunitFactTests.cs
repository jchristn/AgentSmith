namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs every Hugo suite in one NUnit test, preserving suite order and lifecycle hooks.
    /// </summary>
    [TestFixture]
    public sealed class HugoNunitFactTests : TouchstoneNunitBase
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
        [Test]
        public async Task RunAll()
        {
            await RunAllAsync();
        }
    }
}
