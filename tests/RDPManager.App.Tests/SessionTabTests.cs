using RDPManager.App.ViewModels;
using RDPManager.Core.Model;
using RDPManager.Core.Sessions;
using RDPManager.Core.Storage;

namespace RDPManager.App.Tests;

public sealed class SessionTabTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private readonly List<ManualTimer> _timers = [];
    private ConnectionTreeViewModel _tree = null!;
    private MainWindowViewModel _main = null!;
    private ConnectionNode _dc = null!;
    private ConnectionNode _noHost = null!;

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
        _noHost = new ConnectionNode { Name = "no-host" };
        await store.SaveNodeAsync(_dc);
        await store.SaveNodeAsync(_noHost);
        _tree = new ConnectionTreeViewModel(store, _dialogs, "Personal");
        _tree.SetTree(await store.LoadAsync());
        _main = new MainWindowViewModel(
            _tree,
            _dialogs,
            _folder,
            () => throw new NotSupportedException(),
            (_, tick) =>
            {
                var timer = new ManualTimer(tick);
                _timers.Add(timer);
                return timer;
            },
            () => { });
    }

    private (SessionTabViewModel Tab, FakeRemoteSession Session) Connect()
    {
        _main.Connect(_tree.FindItem(_dc.Id));
        var tab = Assert.IsType<SessionTabViewModel>(_main.SelectedTab);
        var session = new FakeRemoteSession();
        tab.Attach(session);
        return (tab, session);
    }

    [Fact]
    public async Task Connect_OpensATabAndStartsConnecting()
    {
        await SetUpAsync();

        var (tab, session) = Connect();

        Assert.Equal("acme-dc01", tab.Title);
        Assert.Equal(SessionState.Connecting, tab.State);
        Assert.True(tab.ShowSession);
        Assert.Equal(1, session.ConnectCalls);
        Assert.Equal("Connecting · dc.acme.test", _main.SessionStatus);
        Assert.True(tab.IsInactive);
    }

    [Fact]
    public async Task Connected_UpdatesStateAndStatusBar()
    {
        await SetUpAsync();
        var (tab, session) = Connect();

        session.RaiseConnected();

        Assert.Equal(SessionState.Connected, tab.State);
        Assert.True(tab.EverConnected);
        Assert.False(tab.IsInactive);
        Assert.Equal("Connected · dc.acme.test", _main.SessionStatus);
    }

    [Fact]
    public async Task ConnectingAgain_SwitchesToTheExistingTab()
    {
        await SetUpAsync();
        var (tab, _) = Connect();
        _main.SelectedTab = null;

        _main.Connect(_tree.FindItem(_dc.Id));

        Assert.Single(_main.Tabs);
        Assert.Same(tab, _main.SelectedTab);
    }

    [Fact]
    public async Task ConnectionWithoutHost_ShowsAnErrorAndOpensNothing()
    {
        await SetUpAsync();

        _main.Connect(_tree.FindItem(_noHost.Id));

        Assert.Empty(_main.Tabs);
        Assert.Contains("has no host", Assert.Single(_dialogs.Errors));
    }

    [Fact]
    public async Task NormalEndAfterConnecting_ShowsDisconnectedWithReconnect()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseConnected();

        session.RaiseDisconnected(3, 5);

        Assert.Equal(SessionState.Disconnected, tab.State);
        Assert.Equal("The session was taken over by another connection to the remote computer.", tab.Message);
        Assert.Null(tab.CloseCountdown); // A session that was connected keeps its tab.
        Assert.Contains(tab, _main.Tabs);

        tab.ReconnectCommand.Execute(null);
        Assert.Equal(SessionState.Connecting, tab.State);
        Assert.Equal(2, session.ConnectCalls);
    }

    [Fact]
    public async Task ErrorAfterConnecting_ShowsErrorWithoutAutoClose()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseConnected();

        session.RaiseDisconnected(2308);

        Assert.Equal(SessionState.Error, tab.State);
        Assert.Null(tab.CloseCountdown);
    }

    [Fact]
    public async Task NeverConnected_ShowsErrorAndClosesAfterTenSeconds()
    {
        await SetUpAsync();
        var (tab, session) = Connect();

        session.RaiseDisconnected(0x3000008, 0, "There was a problem connecting to the remote resource.");

        Assert.Equal(SessionState.Error, tab.State);
        Assert.Equal("Couldn't connect", tab.PanelHeading);
        Assert.Equal(10, tab.CloseCountdown);
        Assert.Contains("Closing in 10 s", tab.CountdownText);
        Assert.Equal("Code 0x3000008 (50331656), extended 0", tab.Details);

        var timer = Assert.Single(_timers);
        timer.Tick(9);
        Assert.Equal(1, tab.CloseCountdown);
        Assert.Contains(tab, _main.Tabs);
        timer.Tick();
        Assert.DoesNotContain(tab, _main.Tabs);
    }

    [Fact]
    public async Task ClickingThePanel_StopsTheAutoClose()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseDisconnected(260);

        tab.CancelAutoClose();
        _timers[0].Tick(20);

        Assert.Null(tab.CloseCountdown);
        Assert.Contains(tab, _main.Tabs);
    }

    [Fact]
    public async Task RetryAfterFailure_StopsTheCountdownAndConnectsAgain()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseDisconnected(260);

        tab.ReconnectCommand.Execute(null);

        Assert.True(_timers[0].Disposed);
        Assert.Null(tab.CloseCountdown);
        Assert.Equal(SessionState.Connecting, tab.State);
    }

    [Fact]
    public async Task CancelledBeforeConnecting_ClosesTheTabStraightAway()
    {
        // Covers cancelling the credential prompt and pressing Cancel while connecting.
        await SetUpAsync();
        var (tab, session) = Connect();

        tab.CancelConnectingCommand.Execute(null);
        Assert.Equal(1, session.DisconnectCalls);
        session.RaiseDisconnected(1, 0, null, userInitiated: true);

        Assert.DoesNotContain(tab, _main.Tabs);
        Assert.Empty(_timers);
    }

    [Fact]
    public async Task ClosingAConnectedTab_DisconnectsWithoutAsking()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseConnected();
        SessionTabViewModel? closed = null;
        _main.SessionClosed += (_, s) => closed = s;

        await _main.CloseTabAsync(tab);

        Assert.Empty(_main.Tabs);
        Assert.Equal(1, session.DisconnectCalls);
        Assert.Empty(_dialogs.SavePrompts);
        Assert.Same(tab, closed);
    }

    [Fact]
    public async Task DisconnectInFullScreen_AsksToLeaveFullScreen()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseConnected();
        tab.Location = SessionLocation.FullScreen;
        var exitRequests = 0;
        tab.ExitFullScreenRequested += (_, _) => exitRequests++;

        session.RaiseDisconnected(3);

        Assert.Equal(1, exitRequests);
    }

    [Fact]
    public async Task FullScreen_IsOnlyRequestedWhileConnected()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        var requests = 0;
        tab.FullScreenRequested += (_, _) => requests++;

        _main.FullScreenCommand.Execute(null);
        Assert.Equal(0, requests);

        session.RaiseConnected();
        _main.FullScreenCommand.Execute(null);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task CtrlAltBreak_RequestsFullScreenAndLeavesItAgain()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseConnected();
        int enter = 0, leave = 0;
        tab.FullScreenRequested += (_, _) => enter++;
        tab.ExitFullScreenRequested += (_, _) => leave++;

        session.RaiseGoFullScreen();
        tab.Location = SessionLocation.FullScreen;
        session.RaiseGoFullScreen(); // Already full screen: ignored.
        session.RaiseLeaveFullScreen();

        Assert.Equal(1, enter);
        Assert.Equal(1, leave);
    }

    [Fact]
    public async Task Settings_FromTheErrorPanel_OpensTheConnectionsSettings()
    {
        await SetUpAsync();
        var (tab, session) = Connect();
        session.RaiseDisconnected(260);

        tab.OpenSettingsCommand.Execute(null);

        Assert.Null(tab.CloseCountdown);
        Assert.IsType<SettingsTabViewModel>(_main.SelectedTab);
    }
}
