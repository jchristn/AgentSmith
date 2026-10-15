namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;
    using global::Xunit;

    /// <summary>
    /// Runs each Hugo test case as its own xUnit theory row.
    /// </summary>
    public sealed class HugoTheoryTests
    {
        /// <summary>
        /// Every test case.
        /// </summary>
        public static TouchstoneTheoryData Cases
        {
            get
            {
                return new TouchstoneTheoryData(HugoSuites.All);
            }
        }

        /// <summary>
        /// Run one test case.
        /// </summary>
        /// <param name="testCase">Test case.</param>
        /// <returns>Task.</returns>
        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await TestExecutor.ExecuteCaseAsync(testCase, CancellationToken.None);
        }
    }
}
