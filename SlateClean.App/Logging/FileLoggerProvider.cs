using System.IO;
using Microsoft.Extensions.Logging;

namespace SlateClean.App.Logging;

// Minimal append-only file logger so manual verification can tail
// %LocalAppData%/SlateClean/logs/slateclean.log via PowerShell's
// `Get-Content -Wait`. A WinExe has no inherited console when launched
// from a terminal, so AddSimpleConsole's output goes nowhere on its own.
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlateClean",
            "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "slateclean.log");
    }

    public string LogPath => _path;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _path, _gate);

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _path;
        private readonly object _gate;

        public FileLogger(string category, string path, object gate)
        {
            _category = category;
            _path = path;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            var line = $"{DateTime.Now:HH:mm:ss.fff} {logLevel,-11} {_category}: {message}";
            if (exception is not null) line += " | " + exception;
            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
    }
}
