// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// ChatHandler.ExtractJsonPayload, ParseFallbackPlan, and NormalizeGeneratedContext in
// src/Tablix.Server/Handlers/ChatHandler.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: static helpers over System.Text.Json with typed classes; reasoning blocks stripped; SQL fences
// and trailing terminators handled. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Helpers for reading model output: JSON payloads, generated context, and SQL text. Thread-safe.
    /// </summary>
    public static class ModelText
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract the outermost JSON object from model text (inside a code fence or not).
        /// </summary>
        /// <param name="text">Model text.</param>
        /// <returns>JSON text, or null.</returns>
        public static string? ExtractJsonPayload(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string normalized = StripReasoning(text).Trim();
            Match fenced = Regex.Match(normalized, "```(?:json)?\\s*(?<json>[\\s\\S]*?)```", RegexOptions.IgnoreCase);
            if (fenced.Success) normalized = fenced.Groups["json"].Value.Trim();
            int start = normalized.IndexOf('{');
            int end = normalized.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            return normalized.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Parse a typed object from model text.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="text">Model text.</param>
        /// <returns>Object, or null when no valid JSON object is present.</returns>
        public static T? ParseJson<T>(string? text) where T : class
        {
            string? json = ExtractJsonPayload(text);
            if (json == null) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(json, _Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Normalize generated context: drop reasoning blocks and a wrapping Markdown fence, trim.
        /// </summary>
        /// <param name="text">Model text.</param>
        /// <returns>Context, or an empty string.</returns>
        public static string NormalizeGeneratedContext(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return String.Empty;
            string normalized = StripReasoning(text).Trim();
            if (normalized.StartsWith("```", StringComparison.Ordinal))
            {
                normalized = Regex.Replace(normalized, "^```(?:markdown|md|text)?\\s*", String.Empty, RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, "\\s*```$", String.Empty);
            }
            return normalized.Trim();
        }

        /// <summary>
        /// Normalize generated SQL: drop a code fence and a single trailing terminator.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <returns>SQL.</returns>
        public static string NormalizeSql(string? sql)
        {
            if (String.IsNullOrWhiteSpace(sql)) return String.Empty;
            string s = sql.Trim();
            Match fenced = Regex.Match(s, "^```(?:sql)?\\s*(?<sql>[\\s\\S]*?)\\s*```$", RegexOptions.IgnoreCase);
            if (fenced.Success) s = fenced.Groups["sql"].Value.Trim();
            return Vendored.Tablix.QueryValidator.NormalizeSingleStatement(s);
        }

        #endregion

        #region Private-Methods

        private static string StripReasoning(string text)
        {
            return Regex.Replace(text, "<think>[\\s\\S]*?</think>", String.Empty, RegexOptions.IgnoreCase);
        }

        #endregion
    }
}
