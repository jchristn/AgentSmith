namespace Test.Automated
{
    /// <summary>
    /// Outcome of one test.
    /// </summary>
    public enum TestOutcomeEnum
    {
        /// <summary>
        /// The test passed.
        /// </summary>
        Passed,

        /// <summary>
        /// The test failed.
        /// </summary>
        Failed,

        /// <summary>
        /// The test was skipped (for example, the server does not implement the route yet).
        /// </summary>
        Skipped
    }
}
