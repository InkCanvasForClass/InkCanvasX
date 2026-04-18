using System;
using System.IO;
using System.Text;

namespace SkiaAnnotate.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory;
    private static readonly string LogFilePath;

    static AppLogger()
    {
        var baseDir = AppContext.BaseDirectory;
        LogDirectory = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(LogDirectory);
        LogFilePath = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public static string CurrentLogDirectory => LogDirectory;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = BuildLine(level, message, ex);
        lock (SyncRoot)
        {
            File.AppendAllText(LogFilePath, line, Encoding.UTF8);
        }
    }

    private static string BuildLine(string level, string message, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.Append('[')
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append("] [")
            .Append(level)
            .Append("] ")
            .Append(message);

        if (ex != null)
        {
            sb.AppendLine()
                .Append(ex);
        }

        sb.AppendLine();
        return sb.ToString();
    }
}

