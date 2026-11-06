using RDPManager.App.ViewModels;
using RDPManager.Core.Credentials;
using RDPManager.Core.Model;
using RDPManager.Core.Storage;

namespace RDPManager.App.Tests;

/// <summary>Passwords in settings tabs, tree operations and sessions (DESIGN: Credentials, Windows details).</summary>
public sealed class CredentialsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private readonly InMemoryCredentialStore _credentials = new();
    private FailingStore _store = null!;
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
        _store = new FailingStore(new JsonFileStore(_folder));
        await _store.LoadAsync();
        _acme = new FolderNode { Name = "Acme" };
        _dc = new ConnectionNode { Name = "acme-dc01" };
        _dc.Settings["host"] = "dc.acme.test";
        await _store.SaveNodeAsync(_acme);
        await _store.SaveNodeAsync(_dc, _acme.Id);

        _tree = new ConnectionTreeViewModel(_store, new CredentialService(_credentials), _dialogs, "Personal");
        _tree.SetTree(await _store.LoadAsync());
        _main = new MainWindowViewModel(_tree, _dialogs, _folder, () => throw new NotSupportedException(), (_, _) => new ManualTimer(), () => { });
    }

    private static string Target(string id, string key = "password") => CredentialService.Target(id, key);

    private SettingsTabViewModel OpenSettings(string id)
    {
        _main.OpenSettings(_tree.FindItem(id));
        return Assert.IsType<SettingsTabViewModel>(_main.SelectedTab);
    }

    private static SettingFieldViewModel Field(SettingsTabViewModel tab, string key) => tab.Fields.Single(f => f.Key == key);

    private async Task<TreeNode> ReloadAsync(string id) => (await new JsonFileStore(_folder).LoadAsync()).Find(id)!;

    private async Task StorePasswordAsync(string id, string password, string key = "password")
    {
        var tab = OpenSettings(id);
        Field(tab, key).Password = password;
        await tab.SaveAsync();
        await _main.CloseTabAsync(tab);
    }

    [Fact]
    public async Task PasswordField_StartsNotStored()
    {
        await SetUpAsync();

        var field = Field(OpenSettings(_dc.Id), "password");

        Assert.True(field.IsSecret);
        Assert.Equal(string.Empty, field.Password);
        Assert.Equal("Not stored", field.SourceText);
        Assert.Equal("Not stored: Windows will ask", field.PasswordPlaceholder);
        Assert.False(field.CanReset);
    }

    [Fact]
    public async Task TypingAPassword_StoresItOnSave_AndOnlyTheMarkerGoesToTheFile()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        var field = Field(tab, "password");

        field.Password = "P@ss";
        Assert.True(tab.IsModified);
        Assert.Equal("Stored in Credential Manager on Save", field.SourceText);
        Assert.Empty(_credentials.Entries); // Nothing until Save.

        await tab.SaveAsync();

        Assert.Equal("P@ss", _credentials.Read(Target(_dc.Id)));
        Assert.Equal("acme-dc01", _credentials.CommentFor(Target(_dc.Id)));
        var saved = await ReloadAsync(_dc.Id);
        Assert.True(saved.Settings["passwordStored"]!.GetValue<bool>());
        Assert.DoesNotContain("P@ss", await File.ReadAllTextAsync(Path.Combine(_folder, "connections.json")));

        Assert.False(tab.IsModified);
        Assert.Equal(string.Empty, field.Password); // Never read back into the field.
        Assert.Equal("●●●●●●●●", field.PasswordPlaceholder);
        Assert.Equal("Stored in Credential Manager", field.SourceText);
        Assert.True(field.CanReset);
    }

    [Fact]
    public async Task ClearingTheTypedPassword_LeavesNothingToSave()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        var field = Field(tab, "password");

        field.Password = "P@ss";
        field.Password = string.Empty;

        Assert.False(tab.IsModified);
    }

    [Fact]
    public async Task FolderPassword_IsInheritedByItsConnections()
    {
        await SetUpAsync();
        await StorePasswordAsync(_acme.Id, "folder-pw");

        var folderField = Field(OpenSettings(_acme.Id), "password");
        Assert.Equal("Stored in Credential Manager · used by 1 connection", folderField.SourceText);

        var field = Field(OpenSettings(_dc.Id), "password");
        Assert.Equal("Inherited from Acme", field.SourceText);
        Assert.True(field.HasSourceLink);
        Assert.True(field.IsInherited);
        Assert.Equal("●●●●●●●●", field.PasswordPlaceholder);
    }

    [Fact]
    public async Task Reset_RemovesThePasswordOnSave()
    {
        await SetUpAsync();
        await StorePasswordAsync(_dc.Id, "P@ss");
        var tab = OpenSettings(_dc.Id);
        var field = Field(tab, "password");

        field.ResetCommand.Execute(null);
        Assert.Equal("Removed on Save", field.SourceText);
        Assert.NotNull(_credentials.Read(Target(_dc.Id))); // Still there until Save.

        await tab.SaveAsync();

        Assert.Null(_credentials.Read(Target(_dc.Id)));
        Assert.False((await ReloadAsync(_dc.Id)).Settings.ContainsKey("passwordStored"));
    }

    [Fact]
    public async Task FailedFileSave_PutsThePreviousPasswordBack()
    {
        await SetUpAsync();
        await StorePasswordAsync(_dc.Id, "old-pw");
        var tab = OpenSettings(_dc.Id);
        Field(tab, "password").Password = "new-pw";

        _store.Fail = true;
        await tab.SaveAsync();

        Assert.Equal("old-pw", _credentials.Read(Target(_dc.Id)));
        Assert.True(tab.IsModified);
        Assert.Contains("could not be saved", Assert.Single(_dialogs.Errors));
    }

    [Fact]
    public async Task FailedCredentialWrite_SavesNothing()
    {
        await SetUpAsync();
        var tab = OpenSettings(_dc.Id);
        Field(tab, "password").Password = "P@ss";
        _credentials.FailFor = Target(_dc.Id);

        await tab.SaveAsync();

        Assert.Contains("password could not be stored", Assert.Single(_dialogs.Errors));
        Assert.False((await ReloadAsync(_dc.Id)).Settings.ContainsKey("passwordStored"));
        Assert.True(tab.IsModified);
    }

    [Fact]
    public async Task NewConnection_StoresItsPasswordUnderItsNewId()
    {
        await SetUpAsync();

        _main.NewConnection(_tree.FindItem(_acme.Id));
        var tab = Assert.IsType<SettingsTabViewModel>(_main.SelectedTab);
        tab.Name = "acme-fs01";
        Field(tab, "host").Text = "fs.acme.test";
        Field(tab, "password").Password = "fs-pw";

        await tab.SaveAsync();

        Assert.Equal("fs-pw", _credentials.Read(Target(tab.Draft.Id)));
    }

    [Fact]
    public async Task Delete_RemovesThePasswordsOfEverythingInside()
    {
        await SetUpAsync();
        await StorePasswordAsync(_acme.Id, "folder-pw");
        await StorePasswordAsync(_dc.Id, "gw-pw", "gateway.password");
        _dialogs.Answer = true;

        await _tree.DeleteAsync(_tree.FindItem(_acme.Id));

        Assert.Empty(_credentials.Entries);
    }

    [Fact]
    public async Task Duplicate_GivesTheCopyItsOwnPasswords()
    {
        await SetUpAsync();
        await StorePasswordAsync(_dc.Id, "P@ss");

        await _tree.DuplicateAsync(_tree.FindItem(_dc.Id));

        var copyId = _tree.SelectedItem!.Id!;
        Assert.NotEqual(_dc.Id, copyId);
        Assert.Equal("P@ss", _credentials.Read(Target(copyId)));
        Assert.True((await ReloadAsync(copyId)).Settings["passwordStored"]!.GetValue<bool>());
        Assert.Equal("P@ss", _credentials.Read(Target(_dc.Id)));
    }

    [Fact]
    public async Task FailedDuplicateSave_RemovesTheCopiedPasswords()
    {
        await SetUpAsync();
        await StorePasswordAsync(_dc.Id, "P@ss");
        _store.Fail = true;

        await _tree.DuplicateAsync(_tree.FindItem(_dc.Id));

        Assert.Single(_credentials.Entries);
    }

    [Fact]
    public async Task Connect_PassesTheStoredPasswordsToTheSession()
    {
        await SetUpAsync();
        await StorePasswordAsync(_acme.Id, "server-pw");

        _main.Connect(_tree.FindItem(_dc.Id));

        var session = Assert.IsType<SessionTabViewModel>(_main.SelectedTab);
        Assert.Equal(new SessionSecrets("server-pw", "server-pw"), session.Secrets); // Gateway shares by default.
    }

    [Fact]
    public async Task Connect_WithoutAStoredPassword_LeavesItToWindows()
    {
        await SetUpAsync();

        _main.Connect(_tree.FindItem(_dc.Id));

        Assert.Equal(SessionSecrets.None, Assert.IsType<SessionTabViewModel>(_main.SelectedTab).Secrets);
    }

    /// <summary>Wraps a store so a test can make saves fail.</summary>
    private sealed class FailingStore(IConnectionStore inner) : IConnectionStore
    {
        public bool Fail { get; set; }

        public Task<ConnectionTree> LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);

        public Task SaveNodeAsync(TreeNode node, string? parentId = null, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("Simulated save failure.") : inner.SaveNodeAsync(node, parentId, cancellationToken);

        public Task<bool> DeleteNodeAsync(string id, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("Simulated save failure.") : inner.DeleteNodeAsync(id, cancellationToken);

        public Task MoveNodeAsync(string id, string? newParentId, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("Simulated save failure.") : inner.MoveNodeAsync(id, newParentId, cancellationToken);
    }
}
