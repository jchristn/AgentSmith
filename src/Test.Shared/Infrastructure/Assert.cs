namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// Minimal assertion helpers. Failures throw <see cref="TestAssertionException"/>.
    /// </summary>
    public static class Assert
    {
        /// <summary>
        /// Assert a condition.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="TestAssertionException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new TestAssertionException(message);
        }

        /// <summary>
        /// Assert equality.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="expected">Expected.</param>
        /// <param name="actual">Actual.</param>
        /// <param name="what">What is being compared.</param>
        /// <exception cref="TestAssertionException">Thrown when values differ.</exception>
        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!Object.Equals(expected, actual)) throw new TestAssertionException(what + ": expected '" + expected + "' but was '" + actual + "'.");
        }

        /// <summary>
        /// Assert a value is not null.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="value">Value.</param>
        /// <param name="what">What is being checked.</param>
        /// <returns>The non-null value.</returns>
        /// <exception cref="TestAssertionException">Thrown when the value is null.</exception>
        public static T NotNull<T>(T? value, string what) where T : class
        {
            if (value == null) throw new TestAssertionException(what + " was null.");
            return value;
        }
    }
}
