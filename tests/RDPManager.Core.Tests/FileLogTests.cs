using RDPManager.Core.Logging;

namespace RDPManager.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 11, 6, 9, 30, 15, TimeSpan.FromHours(10));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Write_AppendsToTodaysFile_AndRaisesEntryAdded()
    {
        var log = new FileLog(_folder, () => _now);
        LogEntry? added = null;
        log.EntryAdded += (_, e) => added = e;

        log.Write(LogLevel.Info, "Started");
        log.Write(LogLevel.Error, "Couldn't connect\nCode 0x3");

        var path = Path.Combine(_folder, "logs", "rdpmanager-20261106.log");
        Assert.Equal(path, log.FileFor(_now));
        var lines = File.ReadAllLines(path);
        Assert.Equal("2026-11-06 09:30:15.000 +10:00 [INF] Started", lines[0]);
        Assert.Equal("2026-11-06 09:30:15.000 +10:00 [ERR] Couldn't connect", lines[1]);
        Assert.Equal("    Code 0x3", lines[2]);
        Assert.Equal(new LogEntry(_now, LogLevel.Error, "Couldn't connect\nCode 0x3"), added);
    }

    [Fact]
    public void NewDay_StartsANewFile()
    {
        var log = new FileLog(_folder, () => _now);
        log.Write(LogLevel.Info, "one");
        _now = _now.AddDays(1);
        log.Write(LogLevel.Info, "two");

        Assert.Equal(2, Directory.GetFiles(Path.Combine(_folder, "logs")).Length);
    }

    [Fact]
    public void Creating_RemovesFilesOlderThanFourteenDays()
    {
        var logs = Directory.CreateDirectory(Path.Combine(_folder, "logs")).FullName;
        File.WriteAllText(Path.Combine(logs, "rdpmanager-20261023.log"), "15 days old");
        File.WriteAllText(Path.Combine(logs, "rdpmanager-20261024.log"), "14 days old: kept");
        File.WriteAllText(Path.Combine(logs, "notes.txt"), "not a log");

        _ = new FileLog(_folder, () => _now);

        Assert.Equal<string[]>(
            ["notes.txt", "rdpmanager-20261024.log"],
            Directory.GetFiles(logs).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }
}
