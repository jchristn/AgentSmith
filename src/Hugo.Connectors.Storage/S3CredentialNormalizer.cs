namespace Hugo.Connectors.Storage
{
    using System;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Decodes copied JSON escapes only when the result is an AWS key value. Stored secrets are unchanged.
    /// </summary>
    internal static class S3CredentialNormalizer
    {
        internal static string? Normalize(string? value, bool amazonS3)
        {
            if (!amazonS3 || String.IsNullOrEmpty(value) || !value.Contains('\\')) return value;
            try
            {
                string? decoded = JsonSerializer.Deserialize<string>("\"" + value + "\"");
                if (!String.IsNullOrEmpty(decoded) && decoded.All(c => Char.IsAsciiLetterOrDigit(c) || c == '+' || c == '/' || c == '='))
                    return decoded;
            }
            catch (JsonException)
            {
                // Preserve malformed escapes and literal backslashes rather than guessing at credential values.
            }
            return value;
        }
    }
}
