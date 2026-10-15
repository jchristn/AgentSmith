namespace Hugo.Connectors.Sql
{
    using System;

    /// <summary>
    /// One named parameter value bound to a statement. Not thread-safe for mutation.
    /// </summary>
    public class SqlParameterValue
    {
        /// <summary>
        /// Parameter name without a prefix (the SQL refers to it as <c>@name</c>).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Value: <see cref="string"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="double"/>,
        /// <see cref="bool"/>, or null for SQL NULL.
        /// </summary>
        public object? Value { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SqlParameterValue()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="name">Name without prefix.</param>
        /// <param name="value">Value.</param>
        public SqlParameterValue(string name, object? value)
        {
            Name = name ?? String.Empty;
            Value = value;
        }
    }
}
