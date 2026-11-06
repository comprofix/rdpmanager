using RDPManager.App.Services;
using RDPManager.App.ViewModels;
using RDPManager.Core;
using RDPManager.Core.Credentials;
using RDPManager.Core.Logging;
using RDPManager.Core.Storage;

namespace RDPManager.App.Tests;

internal sealed class FakeUpdates : IUpdateService
{
    public bool CanUpdate { get; set; } = true;

    public string? Available { get; set; }

    public Exception? CheckFailure { get; set; }

    public int Checks { get; private set; }

    public int Restarts { get; private set; }

    public Task<string?> CheckAsync()
    {
        Checks++;
        return CheckFailure is { } ex ? Task.FromException<string?>(ex) : Task.FromResult(Available);
    }

    public Task DownloadAndRestartAsync()
    {
        Restarts++;
        return Task.CompletedTask;
    }
}

/// <summary>Help › Check for updates, the start-up check and About (DESIGN: Milestone 9 decisions).</summary>
public sealed class UpdatesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeUpdates _updates = new();
    private readonly RecordingLog _log = new();
    private readonly List<string> _openedUrls = [];
    private MainWindowViewModel _main = null!;

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
        var tree = new ConnectionTreeViewModel(store, new CredentialService(new InMemoryCredentialStore()), _dialogs, "Personal");
        tree.SetTree(await store.LoadAsync());
        _main = new MainWindowViewModel(tree, _dialogs, _folder, () => throw new NotSupportedException(), (_, _) => new ManualTimer(), () => { }, _log, null, _updates, _openedUrls.Add);
    }

    [Fact]
    public async Task Manual_LatestVersion_SaysSo()
    {
        await SetUpAsync();

        await _main.CheckForUpdatesAsync();

        Assert.Equal($"You have the latest version ({AppInfo.Version}).", Assert.Single(_dialogs.Infos));
        Assert.Contains(_log.Entries, e => e.Message.StartsWith("Checked for updates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manual_NewVersion_AsksBeforeDownloading()
    {
        await SetUpAsync();
        _updates.Available = "9.9.9";

        await _main.CheckForUpdatesAsync();
        Assert.Contains("RDPManager 9.9.9 is available", Assert.Single(_dialogs.Questions));
        Assert.Equal(0, _updates.Restarts); // Answered No.

        _dialogs.Answer = true;
        await _main.CheckForUpdatesAsync();
        Assert.Equal(1, _updates.Restarts);
    }

    [Fact]
    public async Task Manual_Failure_IsShownAsAnError()
    {
        await SetUpAsync();
        _updates.CheckFailure = new HttpRequestException("No network.");

        await _main.CheckForUpdatesAsync();

        Assert.Contains("No network.", Assert.Single(_dialogs.Errors));
    }

    [Fact]
    public async Task Manual_OnADevelopmentCopy_ExplainsAndChecksNothing()
    {
        await SetUpAsync();
        _updates.CanUpdate = false;

        await _main.CheckForUpdatesAsync();

        Assert.Contains("can't update itself", Assert.Single(_dialogs.Infos));
        Assert.Equal(0, _updates.Checks);
    }

    [Fact]
    public async Task AtStart_IsQuietUnlessAnUpdateExists()
    {
        await SetUpAsync();

        await _main.CheckForUpdatesAtStartAsync();
        _updates.CheckFailure = new HttpRequestException("No network.");
        await _main.CheckForUpdatesAtStartAsync();

        Assert.Empty(_dialogs.Infos);
        Assert.Empty(_dialogs.Errors);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("No network.", StringComparison.Ordinal));

        _updates.CheckFailure = null;
        _updates.Available = "9.9.9";
        await _main.CheckForUpdatesAtStartAsync();
        Assert.Single(_dialogs.Questions);
    }

    [Fact]
    public async Task About_ShowsVersionLicenceAndRepository_AndGitHubOpensIt()
    {
        await SetUpAsync();

        _main.AboutCommand.Execute(null);
        _main.OpenRepositoryCommand.Execute(null);

        var about = Assert.Single(_dialogs.Infos);
        Assert.Contains(AppInfo.Version, about);
        Assert.Contains("MIT licence", about);
        Assert.Contains("https://github.com/comprofix/rdpmanager", about);
        Assert.Equal(["https://github.com/comprofix/rdpmanager"], _openedUrls);
    }

    [Fact]
    public async Task ProgramSettings_UpdateCheckIsOffByDefault_AndSavedWithSave()
    {
        var store = new ProgramSettingsStore(_folder);
        var tab = new ProgramSettingsTabViewModel(store, new ProgramSettings(), _dialogs, _ => { }, canSave: true);

        Assert.False(tab.CheckForUpdatesAtStart);
        tab.CheckForUpdatesAtStart = true;
        Assert.True(tab.IsModified);

        await tab.SaveAsync();

        Assert.True((await store.LoadAsync()).CheckForUpdatesAtStart);
        Assert.False(tab.IsModified);
    }
}
