namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// Raised when a test assertion fails.
    /// </summary>
    public class TestAssertionException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        public TestAssertionException(string message) : base(message)
        {
        }
    }
}
