namespace Test.Automated
{
    using System;

    /// <summary>
    /// Thrown by a test to mark itself skipped.
    /// </summary>
    public class TestSkippedException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="reason">Why the test was skipped.</param>
        public TestSkippedException(string reason) : base(reason)
        {
        }
    }
}
