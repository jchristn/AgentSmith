namespace Test.Shared.Connectors.Aws
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Assertions for the AWS connector suites. Each throws <see cref="InvalidOperationException"/> on failure.
    /// </summary>
    public static class AwsAssert
    {
        /// <summary>
        /// Assert a condition.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert two values are equal.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="expected">Expected.</param>
        /// <param name="actual">Actual.</param>
        /// <param name="what">What is compared.</param>
        /// <exception cref="InvalidOperationException">Thrown when they differ.</exception>
        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual)) throw new InvalidOperationException("Assertion failed: " + what + " expected '" + expected + "' but was '" + actual + "'");
        }

        /// <summary>
        /// Assert a result succeeded.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="what">What was called.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result is an error.</exception>
        public static void Ok(ToolResult result, string what)
        {
            if (result.IsError) throw new InvalidOperationException("Assertion failed: " + what + " returned error " + result.ErrorCode + ": " + result.Text);
        }

        /// <summary>
        /// Assert a result failed with a code.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="code">Expected code.</param>
        /// <param name="what">What was called.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result differs.</exception>
        public static void Error(ToolResult result, string code, string what)
        {
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + what + " expected error " + code + " but got " + (result.IsError ? "error " + result.ErrorCode : "success") + ": " + result.Text);
        }

        /// <summary>
        /// Assert text contains a value.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="value">Value.</param>
        /// <param name="what">What is checked.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value is missing.</exception>
        public static void Contains(string? text, string value, string what)
        {
            if (text == null || !text.Contains(value, StringComparison.Ordinal))
                throw new InvalidOperationException("Assertion failed: " + what + " should contain '" + value + "' but was: " + (text ?? "(null)"));
        }

        /// <summary>
        /// Assert text does not contain a value.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="value">Value.</param>
        /// <param name="what">What is checked.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value is present.</exception>
        public static void NotContains(string? text, string value, string what)
        {
            if (text != null && text.Contains(value, StringComparison.Ordinal))
                throw new InvalidOperationException("Assertion failed: " + what + " should not contain '" + value + "' but was: " + text);
        }
    }
}
