using RDPManager.App.ViewModels;
using RDPManager.Core.Credentials;
using RDPManager.Core.Model;
using RDPManager.Core.Storage;

namespace RDPManager.App.Tests;

/// <summary>The exit prompt (DESIGN: Editing behaviour): asked only when something would be lost.</summary>
public sealed class ExitPromptTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private ConnectionTreeViewModel _tree = null!;
    private MainWindowViewModel _main = null!;
    private ConnectionNode _dc = null!;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task SetUpAsync()
    {
        var store = new JsonFileStore(_folder);
        await store.LoadAsync();
        _dc = new ConnectionNode { Name = "acme-dc01" };
        _dc.Settings["host"] = "dc.acme.test";
        await store.SaveNodeAsync(_dc);
        _tree = new ConnectionTreeViewModel(store, new CredentialService(new InMemoryCredentialStore()), _dialogs, "Personal");
        _tree.SetTree(await store.LoadAsync());
        _main = new MainWindowViewModel(_tree, _dialogs, _folder, () => throw new NotSupportedException(), (_, _) => new ManualTimer(), () => { });
    }

    private (SessionTabViewModel Tab, FakeRemoteSession Session) Connect()
    {
        _main.Connect(_tree.FindItem(_dc.Id));
        var tab = Assert.IsType<SessionTabViewModel>(_main.SelectedTab);
        var session = new FakeRemoteSession();
        tab.Attach(session);
        session.RaiseConnected();
        return (tab, session);
    }

    [Fact]
    public async Task NothingToLose_ExitsWithoutAsking()
    {
        await SetUpAsync();
        _main.OpenSettings(_tree.FindItem(_dc.Id)); // Open but unchanged.

        Assert.True(_main.ConfirmExit());
        Assert.Empty(_dialogs.ExitPrompts);
    }

    [Fact]
    public async Task UnsavedSettings_AreListed_AndCancelKeepsTheAppOpen()
    {
        await SetUpAsync();
        _main.OpenSettings(_tree.FindItem(_dc.Id));
        var tab = Assert.IsType<SettingsTabViewModel>(_main.SelectedTab);
        tab.Name = "acme-dc02";

        Assert.False(_main.ConfirmExit());

        var prompt = Assert.Single(_dialogs.ExitPrompts);
        Assert.Contains("unsaved changes", prompt);
        Assert.Contains("acme-dc01 - Settings", prompt);
        Assert.DoesNotContain("session", prompt);
    }

    [Fact]
    public async Task OpenSessions_AreCounted_AndDisconnectedOnExit()
    {
        await SetUpAsync();
        var (_, session) = Connect();
        _dialogs.ExitAnswer = true;

        Assert.True(_main.ConfirmExit());

        Assert.Contains("1 open session will be disconnected.", Assert.Single(_dialogs.ExitPrompts));
        Assert.Equal(1, session.DisconnectCalls);
    }

    [Fact]
    public async Task CancelledExit_LeavesSessionsConnected()
    {
        await SetUpAsync();
        var (_, session) = Connect();

        Assert.False(_main.ConfirmExit());
        Assert.Equal(0, session.DisconnectCalls);
    }

    [Fact]
    public async Task DisconnectedSessions_DoNotNeedAPrompt()
    {
        await SetUpAsync();
        var (_, session) = Connect();
        session.RaiseDisconnected(3);

        Assert.True(_main.ConfirmExit());
        Assert.Empty(_dialogs.ExitPrompts);
    }
}
