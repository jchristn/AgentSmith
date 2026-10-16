namespace Hugo.Core.Telemetry
{
    using System;
    using System.Diagnostics;
    using Microsoft.Extensions.Logging;

    /// <summary>Immutable-by-convention bounded log export envelope. Never holds an original exception or property object.</summary>
    internal class HugoLogRecord
    {
        internal string Message { get; set; } = String.Empty;
        internal string Details { get; set; } = String.Empty;
        internal string Level { get; set; } = String.Empty;
        internal string TraceId { get; set; } = String.Empty;
        internal string SpanId { get; set; } = String.Empty;
        internal string Node { get; set; } = String.Empty;
        internal LogLevel Severity { get; set; }
        internal Activity? Activity { get; set; }
    }
}
