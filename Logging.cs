using System.Globalization;
using System.Reflection;
using System.Text;

namespace PcanCanMonitor;

internal static class AppLog
{
    private static readonly object Sync = new();
    private static string? _logPath;

    public static string LogPath => _logPath ?? GetDefaultPath();

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_logPath is not null)
                return;

            _logPath = GetDefaultPath();
            if (!TryPreparePath(_logPath))
            {
                _logPath = Path.Combine(AppContext.BaseDirectory, "pcan-monitor.log");
                if (!TryPreparePath(_logPath))
                    _logPath = Path.Combine(Path.GetTempPath(), "PcanCanMonitor", "pcan-monitor.log");
                if (!TryPreparePath(_logPath))
                {
                    _logPath = null;
                    return;
                }
            }

            WriteCore("INFO", $"Starting {Assembly.GetEntryAssembly()?.GetName().Name ?? "PCAN CAN Monitor"}.");
            WriteCore("INFO", $"Log file: {_logPath}");
            WriteCore("INFO", $"OS: {Environment.OSVersion}; .NET: {Environment.Version}; 64-bit: {Environment.Is64BitProcess}");
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                if (_logPath is null)
                    Initialize();
                WriteCore(level, message);
            }
            catch
            {
                try
                {
                    _logPath = Path.Combine(Path.GetTempPath(), "PcanCanMonitor", "pcan-monitor.log");
                    TryPreparePath(_logPath);
                    WriteCore(level, message);
                }
                catch
                {
                    // Last-resort logging is intentionally best effort.
                }
            }
        }
    }

    private static bool TryPreparePath(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return false;
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, Environment.NewLine, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteCore(string level, string message)
    {
        if (_logPath is null)
            return;
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}";
        File.AppendAllText(_logPath, line, Encoding.UTF8);
    }

    private static string GetDefaultPath()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcanCanMonitor");
        return Path.Combine(directory, "pcan-monitor.log");
    }
}
