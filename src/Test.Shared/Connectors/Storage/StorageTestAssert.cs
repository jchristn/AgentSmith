namespace Test.Shared.Connectors.Storage
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Assertion helpers for the storage suites.
    /// </summary>
    public static class StorageTestAssert
    {
        /// <summary>
        /// Fail when the condition is false.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Fail when two values differ.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the values differ.</exception>
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!Object.Equals(expected, actual))
                throw new InvalidOperationException("Assertion failed: " + message + " (expected '" + expected + "', actual '" + actual + "')");
        }

        /// <summary>
        /// Fail unless the result succeeded.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result is an error.</exception>
        public static void Ok(ToolResult result, string message)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.IsError)
                throw new InvalidOperationException("Assertion failed: " + message + " (error " + result.ErrorCode + ": " + result.Text + ")");
        }

        /// <summary>
        /// Fail unless the result is an error with the given code.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="code">Expected error code.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result is not that error.</exception>
        public static void Error(ToolResult result, string code, string message)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + message + " (expected error " + code + ", got " + (result.IsError ? "error " + result.ErrorCode : "success") + ": " + result.Text + ")");
        }
    }
}
