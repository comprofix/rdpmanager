using RDPManager.App.Services;
using RDPManager.App.ViewModels;
using RDPManager.Core.Model;
using RDPManager.Core.Storage;

namespace RDPManager.App.Tests;

public sealed class SettingsTabsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private readonly List<string> _appliedThemes = [];
    private JsonFileStore _store = null!;
    private ConnectionTreeViewModel _tree = null!;
    private MainWindowViewModel _main = null!;
    private FolderNode _acme = null!;
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
        _store = new JsonFileStore(_folder);
        await _store.LoadAsync();
        _acme = new FolderNode { Name = "Acme" };
        _acme.Settings["username"] = "svc-admin";
        _dc = new ConnectionNode { Name = "acme-dc01" };
        _dc.Settings["host"] = "dc.acme.test";
        await _store.SaveNodeAsync(_acme);
        await _store.SaveNodeAsync(_dc, _acme.Id);

        _tree = new ConnectionTreeViewModel(_store, _dialogs, "Personal");
        _tree.SetTree(await _store.LoadAsync());
        var settingsStore = new ProgramSettingsStore(_folder);
        _main = new MainWindowViewModel(
            _tree,
            _dialogs,
            _folder,
            () => new ProgramSettingsTabViewModel(settingsStore, new ProgramSettings(), _dialogs, _appliedThemes.Add, canSave: true),
            () => { });
    }

    private SettingsTabViewModel OpenSettings(string id)
    {
        _main.OpenSettings(_tree.FindItem(id));
        return Assert.IsType<SettingsTabViewModel>(_main.SelectedTab);
    }

    private static SettingFieldViewModel Field(SettingsTabViewModel tab, string key) => tab.Fields.Single(f => f.Key == key);

    private async Task<ConnectionTree> ReloadAsync() => await new JsonFileStore(_folder).LoadAsync();

    [Fact]
    public async Task OpenSettings_ShowsTitleBreadcrumbAndInheritedValues()
    {
        await SetUpAsync();

        var tab = OpenSettings(_dc.Id);

        Assert.Equal("acme-dc01 - Settings", tab.Title);
        Assert.Equal("Personal › Acme", tab.Breadcrumb);
        Assert.False(tab.IsModified);
        Assert.Equal("No unsaved changes", tab.StatusText);
        Assert.False(tab.SaveCommand.CanExecute(null));

        var username = Field(tab, "username");
        Assert.True(username.IsInherited);
        Assert.Equal("svc-admin", username.Text);
        Assert.Equal("Inherited from Acme", username.SourceText);
        Assert.True(username.HasSourceLink);
        Assert.Equal("Default", Field(tab, "port").SourceText);
        Assert.Equal("dc.acme.test", Field(tab, "host").Text);
    }

    [Fact]
    public async Task OpeningTheSameSettingsAgain_SwitchesToTheExistingTab()
    {
        await SetUpAsync();
        var first = OpenSettings(_dc.Id);
        _main.OpenSettings(_tree.FindItem(_acme.Id));

        _main.OpenSettings(_tree.FindItem(_dc.Id));

        Assert.Equal(2, _main.Tabs.Count);
        Assert.Same(first, _main.SelectedTab);
    }

    [Fact]
    public async Task EditingAField_MarksTheTabModified_AndSaveWritesIt()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        var username = Field(tab, "username");

        username.Text = "dc-admin";

        Assert.True(tab.IsModified);
        Assert.Equal("Unsaved changes", tab.StatusText);
        Assert.False(username.IsInherited);
        Assert.Equal("Set here", username.SourceText);
        Assert.True(username.CanReset);
        Assert.False(_dc.Settings.ContainsKey("username") && _dc.Settings["username"]!.GetValue<string>() == "dc-admin");

        await tab.SaveAsync();

        Assert.False(tab.IsModified);
        Assert.Equal("dc-admin", (await ReloadAsync()).Find(_dc.Id)!.Settings["username"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reset_ReturnsToTheInheritedValue()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        var username = Field(tab, "username");
        username.Text = "dc-admin";

        username.ResetCommand.Execute(null);

        Assert.True(username.IsInherited);
        Assert.Equal("svc-admin", username.Text);
        Assert.False(tab.IsModified);
    }

    [Fact]
    public async Task ChoicesAndTickBoxes_SetValues()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);

        var usage = Field(tab, "gateway.usage");
        Assert.Equal("Never", usage.SelectedChoice!.Label);
        usage.SelectedChoice = usage.Choices.Single(c => c.Value == "bypassLocal");
        Field(tab, "redirect.drives").IsChecked = true;
        await tab.SaveAsync();

        var saved = (await ReloadAsync()).Find(_dc.Id)!;
        Assert.Equal("bypassLocal", saved.Settings["gateway.usage"]!.GetValue<string>());
        Assert.True(saved.Settings["redirect.drives"]!.GetValue<bool>());
    }

    [Fact]
    public async Task InvalidValues_AreReportedAndNotSaved()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        Field(tab, "port").Text = "99999";
        Field(tab, "host").Text = "";

        await tab.SaveAsync();

        var error = Assert.Single(_dialogs.Errors);
        Assert.Contains("Host: enter the computer's name or address.", error);
        Assert.Contains("Port: enter a number from 1 to 65535.", error);
        Assert.True(tab.IsModified);
        Assert.False((await ReloadAsync()).Find(_dc.Id)!.Settings.ContainsKey("port"));
    }

    [Fact]
    public async Task RenamingInTheSettingsTab_ReSortsTheTreeAndUpdatesTheTitle()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);

        tab.Name = "zz-dc";
        Assert.Equal("acme-dc01 - Settings", tab.Title); // Title shows the saved name until Save.
        await tab.SaveAsync();

        Assert.Equal("zz-dc - Settings", tab.Title);
        Assert.Equal("zz-dc", _tree.FindItem(_dc.Id)!.Name);
    }

    [Fact]
    public async Task Cancel_AsksAndDiscardRevertsTheDraft()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        Field(tab, "username").Text = "dc-admin";
        _dialogs.SaveAnswer = SaveChoice.Discard;

        await tab.CancelAsync();

        Assert.Single(_dialogs.SavePrompts);
        Assert.False(tab.IsModified);
        Assert.Equal("svc-admin", Field(tab, "username").Text);
        Assert.Contains(tab, _main.Tabs); // Discard keeps an existing item's tab open.
    }

    [Fact]
    public async Task ClosingWithChanges_CancelKeepsTheTab_SaveSavesAndCloses()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        Field(tab, "username").Text = "dc-admin";

        _dialogs.SaveAnswer = SaveChoice.Cancel;
        await _main.CloseTabAsync(tab);
        Assert.Contains(tab, _main.Tabs);

        _dialogs.SaveAnswer = SaveChoice.Save;
        await _main.CloseTabAsync(tab);
        Assert.DoesNotContain(tab, _main.Tabs);
        Assert.Equal("dc-admin", (await ReloadAsync()).Find(_dc.Id)!.Settings["username"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClosingWithoutChanges_DoesNotAsk()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);

        await _main.CloseTabAsync(tab);

        Assert.Empty(_main.Tabs);
        Assert.Empty(_dialogs.SavePrompts);
    }

    [Fact]
    public async Task NewConnection_IsCreatedOnlyOnSave_InTheSelectedFolder()
    {
        await SetUpAsync();
        _main.NewConnection(_tree.FindItem(_acme.Id));
        var tab = Assert.IsType<SettingsTabViewModel>(_main.SelectedTab);

        Assert.Equal("New connection - Settings", tab.Title);
        Assert.True(tab.IsModified);
        Assert.Equal("svc-admin", Field(tab, "username").Text); // Inherits from Acme already.
        Assert.Single(((FolderNode)(await ReloadAsync()).Find(_acme.Id)!).Children);

        tab.Name = "acme-web01";
        Field(tab, "host").Text = "web.acme.test";
        await tab.SaveAsync();

        var acme = (FolderNode)(await ReloadAsync()).Find(_acme.Id)!;
        Assert.Equal(["acme-dc01", "acme-web01"], acme.Children.Select(c => c.Name));
        Assert.Equal("acme-web01 - Settings", tab.Title);
        Assert.Equal("acme-web01", _tree.SelectedItem!.Name);
    }

    [Fact]
    public async Task NewFolder_DiscardClosesTheTabAndCreatesNothing()
    {
        await SetUpAsync();
        _main.NewFolder(_tree.Items[0]);
        var tab = Assert.IsType<SettingsTabViewModel>(_main.SelectedTab);
        Assert.DoesNotContain(tab.Fields, f => f.Key == "host");
        _dialogs.SaveAnswer = SaveChoice.Discard;

        await tab.CancelAsync();

        Assert.Empty(_main.Tabs);
        Assert.Single((await ReloadAsync()).Roots);
    }

    [Fact]
    public async Task FolderValues_ShowHowManyConnectionsUseThem()
    {
        await SetUpAsync();

        var tab = OpenSettings(_acme.Id);

        Assert.Equal("Set here · used by 1 connection", Field(tab, "username").SourceText);
    }

    [Fact]
    public async Task InheritedFromLink_SelectsTheFolderInTheTree()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);

        Field(tab, "username").GoToSourceCommand.Execute(null);

        Assert.Equal(_acme.Id, _tree.SelectedItem!.Id);
    }

    [Fact]
    public async Task DeletingAnItem_ClosesItsSettingsTab_AndRenameInTreeUpdatesIt()
    {
        await SetUpAsync();
        var folderTab = OpenSettings(_acme.Id);
        var dcTab = OpenSettings(_dc.Id);

        var item = _tree.FindItem(_acme.Id)!;
        _tree.BeginRename(item);
        item.EditName = "Acme Ltd";
        await _tree.CommitRenameAsync(item);
        Assert.Equal("Acme Ltd - Settings", folderTab.Title);
        Assert.Equal("Acme Ltd", folderTab.Name);

        _dialogs.Answer = true;
        await _tree.DeleteAsync(_tree.FindItem(_acme.Id));
        Assert.DoesNotContain(folderTab, _main.Tabs);
        Assert.DoesNotContain(dcTab, _main.Tabs);
    }

    [Fact]
    public async Task Secrets_AreShownDisabledUntilCredentialsArrive()
    {
        await SetUpAsync();
        var password = Field(OpenSettings(_dc.Id), "password");

        Assert.True(password.IsSecret);
        Assert.False(password.CanReset);
        Assert.Contains("milestone 8", password.SourceText);
    }

    [Fact]
    public async Task ProgramSettings_ThemeAppliesOnlyOnSave_AndIsStored()
    {
        await SetUpAsync();
        _main.OpenProgramSettings();
        var tab = Assert.IsType<ProgramSettingsTabViewModel>(_main.SelectedTab);

        tab.SelectedTheme = tab.Themes.Single(t => t.Value == ProgramSettings.Themes.Dark);
        Assert.True(tab.IsModified);
        Assert.Empty(_appliedThemes);

        await tab.SaveAsync();

        Assert.Equal(["dark"], _appliedThemes);
        Assert.False(tab.IsModified);
        Assert.Equal("dark", (await new ProgramSettingsStore(_folder).LoadAsync()).Theme);
    }

    [Fact]
    public async Task ProgramSettings_UnreadableFile_RefusesToSave()
    {
        await SetUpAsync();
        var tab = new ProgramSettingsTabViewModel(new ProgramSettingsStore(_folder), new ProgramSettings(), _dialogs, _appliedThemes.Add, canSave: false);
        tab.SelectedTheme = tab.Themes[0];

        await tab.SaveAsync();

        Assert.Single(_dialogs.Errors);
        Assert.Empty(_appliedThemes);
        Assert.False(File.Exists(Path.Combine(_folder, ProgramSettingsStore.FileName)));
    }
}
