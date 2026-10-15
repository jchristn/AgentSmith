namespace Test.Shared.Connectors.Ssh
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Assertion helpers for the SSH suites.
    /// </summary>
    public static class SshAssert
    {
        /// <summary>
        /// Fail when the condition is false.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Fail unless the result succeeded.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="what">What was called.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result is an error.</exception>
        public static void Ok(ToolResult result, string what)
        {
            if (result.IsError) throw new InvalidOperationException("Assertion failed: " + what + " returned error " + result.ErrorCode + ": " + result.Text);
        }

        /// <summary>
        /// Fail unless the result is an error with the given code.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="code">Expected code.</param>
        /// <param name="what">What was called.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result does not match.</exception>
        public static void Error(ToolResult result, string code, string what)
        {
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + what + " expected error " + code + " but got " + (result.IsError ? "error " + result.ErrorCode : "success") + ": " + result.Text);
        }

        /// <summary>
        /// Fail unless the text contains the value.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="value">Expected substring.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value is missing.</exception>
        public static void Contains(string? text, string value, string what)
        {
            if (text == null || !text.Contains(value, StringComparison.Ordinal))
                throw new InvalidOperationException("Assertion failed: " + what + " should contain '" + value + "' but was: " + (text ?? "(null)"));
        }

        /// <summary>
        /// Fail when the text contains the value.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="value">Unexpected substring.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value is present.</exception>
        public static void NotContains(string? text, string value, string what)
        {
            if (text != null && text.Contains(value, StringComparison.Ordinal))
                throw new InvalidOperationException("Assertion failed: " + what + " should not contain '" + value + "' but was: " + text);
        }
    }
}
