namespace Test.Shared.Agent
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Assertion helpers for the agent suites.
    /// </summary>
    public static class AgentAssert
    {
        /// <summary>
        /// Fail with a message unless the condition holds.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Fail unless two values are equal.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="expected">Expected.</param>
        /// <param name="actual">Actual.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the values differ.</exception>
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual)) throw new InvalidOperationException("Assertion failed: " + message + " (expected '" + expected + "', actual '" + actual + "')");
        }

        /// <summary>
        /// Fail unless the action throws the exception type; returns the exception.
        /// </summary>
        /// <typeparam name="T">Exception type.</typeparam>
        /// <param name="action">Action.</param>
        /// <param name="message">Message.</param>
        /// <returns>The exception.</returns>
        /// <exception cref="InvalidOperationException">Thrown when nothing or something else is thrown.</exception>
        public static async Task<T> ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T e)
            {
                return e;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Assertion failed: " + message + " (threw " + e.GetType().Name + ": " + e.Message + ")");
            }
            throw new InvalidOperationException("Assertion failed: " + message + " (nothing thrown)");
        }
    }
}
