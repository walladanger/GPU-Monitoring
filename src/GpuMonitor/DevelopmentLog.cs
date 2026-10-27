using System.Diagnostics;
using System.IO;

namespace GpuMonitor;

internal static class DevelopmentLog
{
    private static readonly object Gate = new();
    private const long MaximumBytes = 2 * 1024 * 1024;
    internal static string Path { get; private set; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuMonitor", "logs", "development.log");
    internal static void Start(string? path = null)
    {
        if (path != null) Path = path;
        Write("startup", $"GPU Monitor {typeof(Program).Assembly.GetName().Version}; PID {Environment.ProcessId}; .NET {Environment.Version}; OS {Environment.OSVersion}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("fatal", e.ExceptionObject.ToString() ?? "Unknown unhandled error");
        TaskScheduler.UnobservedTaskException += (_, e) => Write("task-error", e.Exception.ToString());
        Trace.Listeners.Add(new LogListener());
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Write("shutdown", "Process exited");
    }
    internal static void Write(string category, string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length >= MaximumBytes) File.Move(Path, Path + ".1", true);
                // Bound entries as well as files. Never record sensor payloads or process inventories.
                if (message.Length > 16384) message = message[..16384] + " [truncated]";
                File.AppendAllText(Path, $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { Debug.WriteLine("Development log unavailable: " + error.Message); }
        }
    }
    private sealed class LogListener : TraceListener
    {
        public override void Write(string? message) => DevelopmentLog.Write("trace", message ?? "");
        public override void WriteLine(string? message) => DevelopmentLog.Write("trace", message ?? "");
    }
}
