namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Security;
    using SyslogLogging;

    /// <summary>
    /// Redacts before console, file, syslog, tracing, and telemetry. Library logging uses a private sinkless ingress:
    /// SyslogLogging 2.4.1 has only a post-write event, so an externally configurable ingress would be unsafe.
    /// The supplied module is an output sink only. Thread-safe; callers must stop producers before disposal.
    /// </summary>
    public class HugoLog : IDisposable
    {
        private static readonly ConditionalWeakTable<LoggingModule, HugoLog> _Adapters = new ConditionalWeakTable<LoggingModule, HugoLog>();
        private readonly LoggingModule _Output;
        private readonly LoggingModule _Ingress;
        private readonly bool _OwnOutput;
        private volatile bool _Disposed;

        /// <summary>Output settings. Changes affect only already scrubbed entries.</summary>
        public SyslogLogging.LoggingSettings Settings { get { return _Output.Settings; } }

        /// <summary>Optional best-effort receiver of scrubbed entries, normally the bounded Radiant log bridge.</summary>
        public Action<LogEntry>? Export { get; set; }

        internal LoggingModule LibraryLogger { get { return _Ingress; } }

        /// <summary>
        /// Wrap a configured output module. Never hand that module to external libraries; Hugo's model factory uses
        /// the separate private ingress. Disable ingress tracing so raw messages cannot reach an activity exporter.
        /// </summary>
        /// <param name="output">Configured console/file/syslog sink.</param>
        /// <param name="ownsOutput">Whether to dispose the supplied sink. Default false.</param>
        /// <exception cref="ArgumentNullException">Thrown when output is null.</exception>
        public HugoLog(LoggingModule output, bool ownsOutput = false)
        {
            _Output = output ?? throw new ArgumentNullException(nameof(output));
            _OwnOutput = ownsOutput;
            _Ingress = new LoggingModule();
            _Ingress.Settings.EnableConsole = false;
            _Ingress.Servers = new List<SyslogServer>();
            _Ingress.Settings.FileLogging = FileLoggingMode.Disabled;
            _Ingress.Settings.EnableTracing = false;
            _Ingress.Settings.EnableMetrics = false;
            _Ingress.Settings.MinimumSeverity = Severity.Debug;
            _Ingress.MessageLogged += Write;
        }

        /// <summary>Adapt an embedding host's logger. Its retained settings configure the output, never raw ingress.</summary>
        /// <param name="output">Output module.</param>
        /// <returns>Cached adapter for the module.</returns>
        /// <exception cref="ArgumentNullException">Thrown when output is null.</exception>
        public static implicit operator HugoLog(LoggingModule output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            lock (_Adapters)
            {
                if (_Adapters.TryGetValue(output, out HugoLog? adapter) && !adapter._Disposed) return adapter;
                _Adapters.Remove(output);
                HugoLog created = new HugoLog(output);
                _Adapters.Add(output, created);
                return created;
            }
        }

        /// <summary>Write a debug entry.</summary><param name="message">Message.</param>
        public void Debug(string message) { Log(Severity.Debug, message); }
        /// <summary>Write an information entry.</summary><param name="message">Message.</param>
        public void Info(string message) { Log(Severity.Info, message); }
        /// <summary>Write a warning entry.</summary><param name="message">Message.</param>
        public void Warn(string message) { Log(Severity.Warn, message); }
        /// <summary>Write an error entry.</summary><param name="message">Message.</param>
        public void Error(string message) { Log(Severity.Error, message); }
        /// <summary>Write an alert entry.</summary><param name="message">Message.</param>
        public void Alert(string message) { Log(Severity.Alert, message); }
        /// <summary>Write a critical entry.</summary><param name="message">Message.</param>
        public void Critical(string message) { Log(Severity.Critical, message); }
        /// <summary>Write an emergency entry.</summary><param name="message">Message.</param>
        public void Emergency(string message) { Log(Severity.Emergency, message); }

        /// <summary>Write an entry through the redaction boundary.</summary>
        /// <param name="severity">Severity.</param><param name="message">Message.</param>
        public void Log(Severity severity, string message)
        {
            Write(new LogEntry(severity, message));
        }

        /// <summary>Write a scrubbed local diagnostic without feeding an exporter failure back into that exporter.</summary>
        /// <param name="severity">Severity.</param><param name="message">Diagnostic message.</param>
        public void Local(Severity severity, string message)
        {
            WriteCore(new LogEntry(severity, message), false);
        }

        /// <summary>Write a structured entry. The caller's entry is never passed to a sink.</summary>
        /// <param name="entry">Entry.</param>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public void LogEntry(LogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            Write(entry);
        }

        /// <summary>Write a structured entry with caller cancellation.</summary>
        /// <param name="entry">Entry.</param><param name="token">Cancellation token.</param>
        /// <returns>Completed task after local sinks receive the entry.</returns>
        public Task LogEntryAsync(LogEntry entry, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            LogEntry(entry);
            return Task.CompletedTask;
        }

        /// <summary>Stop library ingress and release owned sinks.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Dispose implementation.</summary><param name="disposing">Whether managed resources may be released.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (!disposing) return;
            lock (_Adapters)
            {
                if (_Adapters.TryGetValue(_Output, out HugoLog? adapter) && ReferenceEquals(adapter, this)) _Adapters.Remove(_Output);
            }
            Export = null;
            _Ingress.MessageLogged -= Write;
            _Ingress.Dispose();
            if (_OwnOutput) _Output.Dispose();
        }

        private static bool Sensitive(string key)
        {
            string normalized = key.Replace("-", String.Empty, StringComparison.Ordinal).Replace("_", String.Empty, StringComparison.Ordinal).Replace(".", String.Empty, StringComparison.Ordinal).ToLowerInvariant();
            foreach (string suffix in new[] { "password", "passwd", "pwd", "passphrase", "secret", "secretkey", "apikey", "accesskey", "token", "authorization", "cookie", "privatekey", "connectionstring" })
            {
                if (normalized.EndsWith(suffix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private void Write(LogEntry entry)
        {
            WriteCore(entry, true);
        }

        private void WriteCore(LogEntry entry, bool export)
        {
            if (_Disposed || entry.Severity < _Output.Settings.MinimumSeverity) return;
            try
            {
                SecretScrubber scrubber = SecretScrubber.Shared;
                LogEntry safe = new LogEntry(entry.Severity, scrubber.Scrub(entry.Message) ?? String.Empty);
                safe.Timestamp = entry.Timestamp;
                safe.Source = scrubber.Scrub(entry.Source);
                safe.CorrelationId = scrubber.Scrub(entry.CorrelationId);
                safe.TraceId = scrubber.Scrub(entry.TraceId);
                safe.SpanId = scrubber.Scrub(entry.SpanId);
                safe.ThreadId = entry.ThreadId;
                if (entry.Exception != null) safe.Exception = new Exception(scrubber.Scrub(entry.Exception.ToString()));
                foreach (KeyValuePair<string, object> property in entry.Properties)
                {
                    string key = scrubber.Scrub(property.Key) ?? "property";
                    if (Sensitive(property.Key))
                    {
                        safe.Properties[key] = "[redacted:property]";
                        continue;
                    }
                    string rendered;
                    try { rendered = property.Value is string text ? text : JsonSerializer.Serialize(property.Value); }
                    catch (Exception) { rendered = "[unserializable log property]"; }
                    safe.Properties[key] = scrubber.Scrub(rendered) ?? String.Empty;
                }
                _Output.LogEntry(safe);
                try { if (export) Export?.Invoke(safe); }
                catch (Exception) { }
            }
            catch (Exception)
            {
                // Fail closed. Neither the original entry nor a serializer/sink exception is safe to emit here.
            }
        }
    }
}
