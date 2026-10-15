namespace Test.Automated
{
    using System;

    /// <summary>
    /// Thrown when a test assertion fails.
    /// </summary>
    public class TestAssertionException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">What was expected.</param>
        public TestAssertionException(string message) : base(message)
        {
        }
    }
}
