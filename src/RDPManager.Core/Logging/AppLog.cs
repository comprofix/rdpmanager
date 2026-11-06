using System.Globalization;
using System.Text;

namespace RDPManager.Core.Logging;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>One log entry.</summary>
public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Message);

/// <summary>The application log (DESIGN: Log file). Never pass passwords to it.</summary>
public interface IAppLog
{
    /// <summary>Raised after an entry was written, on the thread that wrote it.</summary>
    event EventHandler<LogEntry>? EntryAdded;

    void Write(LogLevel level, string message);
}

/// <summary>A log that keeps nothing (tests, and before the log folder is known).</summary>
public sealed class NullLog : IAppLog
{
    public static NullLog Instance { get; } = new();

    public event EventHandler<LogEntry>? EntryAdded
    {
        add { }
        remove { }
    }

    public void Write(LogLevel level, string message)
    {
    }
}

/// <summary>
/// Writes one file per day, <c>rdpmanager-yyyyMMdd.log</c>, in a <c>logs</c> folder, and removes files older than
/// <see cref="KeepDays"/> days when created. A log that cannot be written is skipped silently: logging must never
/// stop the app.
/// </summary>
public sealed class FileLog : IAppLog
{
    public const int KeepDays = 14;
    public const string FolderName = "logs";

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;

    public FileLog(string storageFolder, Func<DateTimeOffset>? now = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(storageFolder);
        Folder = Path.Combine(storageFolder, FolderName);
        _now = now ?? (() => DateTimeOffset.Now);
        RemoveOldFiles();
    }

    public event EventHandler<LogEntry>? EntryAdded;

    /// <summary>The folder holding the log files.</summary>
    public string Folder { get; }

    public string FileFor(DateTimeOffset time) =>
        Path.Combine(Folder, $"rdpmanager-{time.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(_now(), level, message ?? string.Empty);
        var tag = level switch
        {
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "INF",
        };

        // Continuation lines are indented so each entry starts with its time.
        var text = new StringBuilder()
            .Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(tag).Append("] ")
            .Append(entry.Message.ReplaceLineEndings(Environment.NewLine + "    "))
            .AppendLine()
            .ToString();

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(FileFor(entry.Time), text, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never stop the app.
            }
        }

        EntryAdded?.Invoke(this, entry);
    }

    private void RemoveOldFiles()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            var oldest = _now().Date.AddDays(-(KeepDays - 1));
            foreach (var file in Directory.EnumerateFiles(Folder, "rdpmanager-*.log"))
            {
                var stamp = Path.GetFileNameWithoutExtension(file)["rdpmanager-".Length..];
                if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < oldest)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Old logs are tidied next time.
        }
    }
}
