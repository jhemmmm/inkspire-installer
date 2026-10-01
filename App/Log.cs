using System.Text;

namespace Inkspire.Panel.App;

/// <summary>Small thread-safe file logger with size rotation. Before <see cref="Configure"/> it writes to %TEMP%.</summary>
public static class Log
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int Keep = 3;
    private static readonly object Gate = new();
    private static string _file = Path.Combine(Path.GetTempPath(), "InkspirePanel.log");

    public static string CurrentFile => _file;

    public static void Configure(string logsDir)
    {
        try
        {
            Directory.CreateDirectory(logsDir);
            lock (Gate) _file = Path.Combine(logsDir, "panel.log");
        }
        catch
        {
            // keep the %TEMP% fallback
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        try
        {
            lock (Gate)
            {
                Rotate();
                File.AppendAllText(_file, line, Encoding.UTF8);
            }
        }
        catch
        {
            // logging must never take the panel down
        }
    }

    private static void Rotate()
    {
        var info = new FileInfo(_file);
        if (!info.Exists || info.Length < MaxBytes) return;
        for (var i = Keep; i >= 1; i--)
        {
            var from = i == 1 ? _file : $"{_file}.{i - 1}";
            var to = $"{_file}.{i}";
            if (!File.Exists(from)) continue;
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
    }
}
