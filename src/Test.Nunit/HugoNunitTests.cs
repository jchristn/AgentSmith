namespace Test.Nunit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs each Hugo test case as its own NUnit test.
    /// </summary>
    [TestFixture]
    public sealed class HugoNunitTests
    {
        /// <summary>
        /// Run one test case.
        /// </summary>
        /// <param name="testCase">Test case.</param>
        /// <returns>Task.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await TestExecutor.ExecuteCaseAsync(testCase, CancellationToken.None).ConfigureAwait(false);
        }

        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(HugoSuites.All);
        }
    }
}
