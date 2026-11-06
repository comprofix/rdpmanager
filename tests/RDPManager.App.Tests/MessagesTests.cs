using RDPManager.App.Services;
using RDPManager.App.ViewModels;
using RDPManager.Core.Credentials;
using RDPManager.Core.Logging;
using RDPManager.Core.Model;
using RDPManager.Core.Storage;

namespace RDPManager.App.Tests;

/// <summary>A log the test can read back.</summary>
internal sealed class RecordingLog : IAppLog
{
    public event EventHandler<LogEntry>? EntryAdded;

    public List<LogEntry> Entries { get; } = [];

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        Entries.Add(entry);
        EntryAdded?.Invoke(this, entry);
    }
}

public sealed class MessagesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLog _log = new();
    private readonly List<string> _openedFolders = [];
    private readonly MessagesViewModel _messages;

    public MessagesTests() => _messages = new MessagesViewModel(_log, Path.Combine(_folder, "logs"), a => a(), _openedFolders.Add);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void WarningsAndErrors_AreListedNewestFirst_InfoIsNot()
    {
        _log.Write(LogLevel.Info, "Started");
        _log.Write(LogLevel.Warning, "Careful");
        _log.Write(LogLevel.Error, "Couldn't connect to dc\nCode 0x3");

        Assert.Equal(2, _messages.Rows.Count);
        Assert.Equal("Error", _messages.Rows[0].Level);
        Assert.Equal("Couldn't connect to dc", _messages.Rows[0].Summary);
        Assert.Equal("Warning", _messages.Rows[1].Level);
    }

    [Fact]
    public void NewErrors_AreCountedUntilThePanelOpens()
    {
        Assert.False(_messages.HasNewErrors);

        _log.Write(LogLevel.Error, "one");
        _log.Write(LogLevel.Warning, "not counted");
        _log.Write(LogLevel.Error, "two");
        Assert.Equal("2 new errors", _messages.NewErrorsText);
        Assert.False(_messages.IsOpen); // It does not open by itself.

        _messages.OpenCommand.Execute(null);
        Assert.False(_messages.HasNewErrors);

        _log.Write(LogLevel.Error, "while open");
        Assert.False(_messages.HasNewErrors);
    }

    [Fact]
    public void Clear_EmptiesThePanel_AndOpenLogFolderOpensIt()
    {
        _log.Write(LogLevel.Error, "one");

        _messages.ClearCommand.Execute(null);
        _messages.OpenLogFolderCommand.Execute(null);

        Assert.Empty(_messages.Rows);
        Assert.False(_messages.HasNewErrors);
        Assert.Equal([_messages.LogFolder], _openedFolders);
        Assert.True(Directory.Exists(_messages.LogFolder));
    }

    [Fact]
    public void Toggle_ChangesTheMenuText()
    {
        Assert.Equal("_Messages", _messages.MenuText);
        _messages.ToggleCommand.Execute(null);
        Assert.Equal("Hide _messages", _messages.MenuText);
    }

    [Fact]
    public void ErrorsShownInDialogs_AreLogged()
    {
        var inner = new FakeDialogs();
        var dialogs = new LoggingDialogService(inner, _log);

        dialogs.ShowError("RDPManager", "The change could not be saved.");

        Assert.Equal("The change could not be saved.", Assert.Single(inner.Errors));
        Assert.Equal(new[] { (LogLevel.Error, "The change could not be saved.") }, _log.Entries.Select(e => (e.Level, e.Message)));
    }

    [Fact]
    public async Task Sessions_LogConnectsAndFailuresWithCodes()
    {
        var store = new JsonFileStore(_folder);
        await store.LoadAsync();
        var dc = new ConnectionNode { Name = "acme-dc01" };
        dc.Settings["host"] = "dc.acme.test";
        await store.SaveNodeAsync(dc);
        var tree = new ConnectionTreeViewModel(store, new CredentialService(new InMemoryCredentialStore()), new FakeDialogs(), "Personal");
        tree.SetTree(await store.LoadAsync());
        var main = new MainWindowViewModel(tree, new FakeDialogs(), _folder, () => throw new NotSupportedException(), (_, _) => new ManualTimer(), () => { }, _log, _messages);

        main.Connect(tree.FindItem(dc.Id));
        var tab = Assert.IsType<SessionTabViewModel>(main.SelectedTab);
        var session = new FakeRemoteSession();
        tab.Attach(session);
        session.RaiseDisconnected(0x3000008, 0, "There was a problem connecting to the remote resource.");
        tab.ReconnectCommand.Execute(null);
        session.RaiseConnected();
        session.RaiseDisconnected(3, 5);

        Assert.Equal(
            [
                (LogLevel.Info, "Connecting to acme-dc01 (dc.acme.test:3389)."),
                (LogLevel.Error, "Couldn't connect to acme-dc01: There was a problem connecting to the remote resource.\nCode 0x3000008 (50331656), extended 0"),
                (LogLevel.Info, "Connecting to acme-dc01 (dc.acme.test:3389)."),
                (LogLevel.Info, "Connected to acme-dc01."),
                (LogLevel.Info, "Disconnected from acme-dc01: The session was taken over by another connection to the remote computer.\nCode 0x3 (3), extended 5"),
            ],
            _log.Entries.Select(e => (e.Level, e.Message)));
        Assert.True(_messages.HasNewErrors);
    }
}
