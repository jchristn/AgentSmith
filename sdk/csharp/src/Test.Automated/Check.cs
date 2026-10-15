namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Sdk;

    /// <summary>
    /// Assertions used by the suites.
    /// </summary>
    public static class Check
    {
        #region Public-Methods

        /// <summary>
        /// Assert a condition.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">What was expected.</param>
        /// <exception cref="TestAssertionException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new TestAssertionException(message);
        }

        /// <summary>
        /// Assert equality.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="what">What is compared.</param>
        /// <exception cref="TestAssertionException">Thrown when the values differ.</exception>
        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual)) throw new TestAssertionException(what + ": expected " + (expected?.ToString() ?? "null") + ", got " + (actual?.ToString() ?? "null"));
        }

        /// <summary>
        /// Assert a non-empty string.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="what">What is checked.</param>
        /// <exception cref="TestAssertionException">Thrown when the value is null or empty.</exception>
        public static void NotEmpty(string? value, string what)
        {
            if (String.IsNullOrEmpty(value)) throw new TestAssertionException(what + " should not be empty");
        }

        /// <summary>
        /// Assert that an operation fails with a Hugo error of a given status (and optionally code).
        /// </summary>
        /// <param name="action">Operation.</param>
        /// <param name="status">Expected HTTP status.</param>
        /// <param name="code">Expected error code, or null for any.</param>
        /// <returns>The exception.</returns>
        /// <exception cref="TestAssertionException">Thrown when the operation succeeds or fails differently.</exception>
        public static async Task<HugoException> FailsAsync(Func<Task> action, int status, string? code = null)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (HugoException e)
            {
                if (e.StatusCode != status) throw new TestAssertionException("expected HTTP " + status + ", got " + e.StatusCode + " (" + e.Code + ": " + e.Message + ")");
                if (code != null && e.Code != code) throw new TestAssertionException("expected error code " + code + ", got " + e.Code);
                return e;
            }
            throw new TestAssertionException("expected HTTP " + status + (code != null ? " " + code : String.Empty) + ", but the call succeeded");
        }

        #endregion
    }
}
