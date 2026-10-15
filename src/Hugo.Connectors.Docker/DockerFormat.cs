namespace Hugo.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Formatting and parsing helpers shared by the Docker tools: short IDs, ages, sizes, ports, and time arguments.
    /// Thread-safe.
    /// </summary>
    public static class DockerFormat
    {
        #region Private-Members

        private static readonly Regex _Duration = new Regex("^([0-9]+)(s|m|h|d)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex _ContainerName = new Regex("^/?[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// A 12-character ID like the Docker CLI prints (the <c>sha256:</c> prefix is removed).
        /// </summary>
        /// <param name="id">ID, or null.</param>
        /// <returns>Short ID.</returns>
        public static string ShortId(string? id)
        {
            string v = id ?? String.Empty;
            if (v.StartsWith("sha256:", StringComparison.Ordinal)) v = v.Substring(7);
            return v.Length > 12 ? v.Substring(0, 12) : v;
        }

        /// <summary>
        /// Format an age (<c>45s</c>, <c>12m</c>, <c>5h</c>, <c>3d</c>).
        /// </summary>
        /// <param name="since">Start time.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <returns>Age.</returns>
        public static string Age(DateTime since, DateTime now)
        {
            if (since == default(DateTime)) return "<unknown>";
            TimeSpan age = now - since.ToUniversalTime();
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            if (age.TotalSeconds < 120) return ((int)age.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
            if (age.TotalMinutes < 120) return ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
            if (age.TotalHours < 48) return ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + "h";
            return ((int)age.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
        }

        /// <summary>
        /// Format a byte count with decimal units, as the Docker CLI does (<c>1.5MB</c>).
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>Text.</returns>
        public static string Bytes(double bytes)
        {
            string[] units = new[] { "B", "kB", "MB", "GB", "TB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1000 && unit < units.Length - 1)
            {
                value /= 1000;
                unit++;
            }
            return (unit == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture)) + units[unit];
        }

        /// <summary>
        /// Format published ports as the Docker CLI does (<c>127.0.0.1:8080-&gt;80/tcp</c>).
        /// </summary>
        /// <param name="ports">Ports, or null.</param>
        /// <returns>Text.</returns>
        public static string Ports(IList<PortSummary>? ports)
        {
            if (ports == null || ports.Count == 0) return String.Empty;
            return String.Join(", ", ports
                .Select(p => p.PublicPort.HasValue
                    ? (String.IsNullOrEmpty(p.IP) ? String.Empty : p.IP + ":") + p.PublicPort.Value.ToString(CultureInfo.InvariantCulture) + "->" + p.PrivatePort.ToString(CultureInfo.InvariantCulture) + "/" + p.Type
                    : p.PrivatePort.ToString(CultureInfo.InvariantCulture) + "/" + p.Type)
                .Distinct());
        }

        /// <summary>
        /// Parse a time argument: seconds before now (<c>300</c>), a duration (<c>10m</c>, <c>2h</c>, <c>1d</c>), or an
        /// ISO 8601 timestamp.
        /// </summary>
        /// <param name="value">Value, or null.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <param name="name">Argument name for errors.</param>
        /// <returns>Time (UTC), or null when the value is empty.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the value cannot be read.</exception>
        public static DateTime? ParseTime(string? value, DateTime now, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            if (Int64.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds) && seconds >= 0)
                return now.AddSeconds(-Math.Min(seconds, 315360000));
            Match m = _Duration.Match(v);
            if (m.Success)
            {
                long n = Math.Min(Int64.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 3650L * 86400L);
                switch (m.Groups[2].Value)
                {
                    case "s": return now.AddSeconds(-n);
                    case "m": return now.AddMinutes(-Math.Min(n, 5256000));
                    case "h": return now.AddHours(-Math.Min(n, 87600));
                    default: return now.AddDays(-Math.Min(n, 3650));
                }
            }
            if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                return parsed.UtcDateTime;
            throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, name + " must be seconds (300), a duration (10m, 2h, 1d), or an ISO 8601 time.");
        }

        /// <summary>
        /// Unix seconds as the Docker API expects them in <c>since</c> and <c>until</c>.
        /// </summary>
        /// <param name="time">Time (UTC).</param>
        /// <returns>Text.</returns>
        public static string UnixSeconds(DateTime time)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether a container reference looks like a name or ID (letters, digits, <c>_ . -</c>, optional leading slash).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsContainerReference(string? value)
        {
            return !String.IsNullOrEmpty(value) && value.Length <= 256 && _ContainerName.IsMatch(value);
        }

        /// <summary>
        /// The primary container name without the leading slash.
        /// </summary>
        /// <param name="names">Names, or null.</param>
        /// <returns>Name.</returns>
        public static string Name(IList<string>? names)
        {
            string? first = names?.FirstOrDefault();
            return String.IsNullOrEmpty(first) ? String.Empty : first.TrimStart('/');
        }

        /// <summary>
        /// Shorten text for summaries.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="max">Maximum characters.</param>
        /// <returns>Shortened single-line text.</returns>
        public static string Shorten(string? value, int max)
        {
            string v = (value ?? String.Empty).Replace('\n', ' ').Replace('\r', ' ');
            return v.Length > max ? v.Substring(0, Math.Max(0, max - 3)) + "..." : v;
        }

        #endregion
    }
}
