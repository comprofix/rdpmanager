using RDPManager.App.Services;
using RDPManager.App.ViewModels;
using RDPManager.Core.Model;
using RDPManager.Core.Storage;

namespace RDPManager.App.Tests;

public sealed class ConnectionTreeViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private JsonFileStore _store = null!;
    private ConnectionTreeViewModel _vm = null!;
    private FolderNode _clients = null!;
    private FolderNode _acme = null!;
    private ConnectionNode _dc = null!;
    private ConnectionNode _fs = null!;

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
        _clients = new FolderNode { Name = "Clients" };
        _acme = new FolderNode { Name = "Acme" };
        _dc = new ConnectionNode { Name = "acme-dc01" };
        _fs = new ConnectionNode { Name = "acme-fs01" };
        await _store.SaveNodeAsync(_clients);
        await _store.SaveNodeAsync(_acme, _clients.Id);
        await _store.SaveNodeAsync(_dc, _acme.Id);
        await _store.SaveNodeAsync(_fs, _acme.Id);

        _vm = new ConnectionTreeViewModel(_store, _dialogs, "Personal");
        _vm.SetTree(await _store.LoadAsync());
    }

    private static async Task<ConnectionTree> ReloadAsync(string folder) => await new JsonFileStore(folder).LoadAsync();

    [Fact]
    public async Task Tree_ShowsSourceWithFoldersAndConnections()
    {
        await SetUpAsync();

        var source = Assert.Single(_vm.Items);
        Assert.True(source.IsSource);
        Assert.Equal("Personal", source.Name);
        var clients = Assert.Single(source.Children);
        Assert.True(clients.IsFolder);
        Assert.Equal(["acme-dc01", "acme-fs01"], clients.Children[0].Children.Select(c => c.Name));
        Assert.Same(source, _vm.SelectedItem);
    }

    [Fact]
    public async Task Rename_SavesTrimmedName()
    {
        await SetUpAsync();
        var item = _vm.FindItem(_dc.Id)!;

        _vm.BeginRename(item);
        Assert.True(item.IsEditing);
        item.EditName = "  acme-dc02  ";
        await _vm.CommitRenameAsync(item);

        Assert.False(item.IsEditing);
        Assert.Equal("acme-dc02", item.Name);
        Assert.Equal("acme-dc02", (await ReloadAsync(_folder)).Find(_dc.Id)!.Name);
    }

    [Fact]
    public async Task Rename_EmptyNameIsRefused()
    {
        await SetUpAsync();
        var item = _vm.FindItem(_dc.Id)!;

        _vm.BeginRename(item);
        item.EditName = "   ";
        await _vm.CommitRenameAsync(item);

        Assert.Equal("acme-dc01", item.Name);
        Assert.Single(_dialogs.Errors);
        Assert.Equal("acme-dc01", (await ReloadAsync(_folder)).Find(_dc.Id)!.Name);
    }

    [Fact]
    public async Task Rename_CancelKeepsName()
    {
        await SetUpAsync();
        var item = _vm.FindItem(_dc.Id)!;

        _vm.BeginRename(item);
        item.EditName = "something else";
        _vm.CancelRename(item);

        Assert.False(item.IsEditing);
        Assert.Equal("acme-dc01", item.Name);
    }

    [Fact]
    public async Task SourceRow_CannotBeRenamedDuplicatedOrDeleted()
    {
        await SetUpAsync();
        var source = _vm.Items[0];

        _vm.BeginRename(source);
        await _vm.DuplicateAsync(source);
        await _vm.DeleteAsync(source);

        Assert.False(source.IsEditing);
        Assert.Empty(_dialogs.Questions);
        Assert.Single((await ReloadAsync(_folder)).Roots);
    }

    [Fact]
    public async Task Duplicate_InsertsCopyAfterOriginalAndSelectsIt()
    {
        await SetUpAsync();

        await _vm.DuplicateAsync(_vm.FindItem(_dc.Id));

        var acme = (FolderNode)(await ReloadAsync(_folder)).Find(_acme.Id)!;
        Assert.Equal(["acme-dc01", "acme-dc01 (copy)", "acme-fs01"], acme.Children.Select(c => c.Name));
        Assert.Equal("acme-dc01 (copy)", _vm.SelectedItem!.Name);
    }

    [Fact]
    public async Task Delete_AsksFirstAndDoesNothingOnNo()
    {
        await SetUpAsync();
        _dialogs.Answer = false;

        await _vm.DeleteAsync(_vm.FindItem(_acme.Id));

        Assert.Contains("\"Acme\" and everything in it (2 connections)", Assert.Single(_dialogs.Questions));
        Assert.NotNull((await ReloadAsync(_folder)).Find(_acme.Id));
    }

    [Fact]
    public async Task Delete_OnYesRemovesAndSelectsParent()
    {
        await SetUpAsync();
        _dialogs.Answer = true;

        await _vm.DeleteAsync(_vm.FindItem(_dc.Id));

        Assert.Null((await ReloadAsync(_folder)).Find(_dc.Id));
        Assert.Equal(_acme.Id, _vm.SelectedItem!.Id);
    }

    [Fact]
    public void DeleteQuestion_DescribesEmptyAndMixedFolders()
    {
        var empty = new FolderNode { Name = "Empty" };
        Assert.Equal("Delete the empty folder \"Empty\"?", ConnectionTreeViewModel.DeleteQuestion(empty));

        var tree = new ConnectionTree();
        var folder = new FolderNode { Name = "F" };
        tree.Add(folder);
        tree.Add(new FolderNode(), folder.Id);
        tree.Add(new ConnectionNode(), folder.Id);
        Assert.Contains("(1 folder and 1 connection)", ConnectionTreeViewModel.DeleteQuestion(folder));
    }

    [Fact]
    public async Task Drop_OntoFolderMovesIntoItAndExpandsIt()
    {
        await SetUpAsync();

        Assert.True(await _vm.DropAsync(_dc.Id, _clients.Id));

        var clients = (FolderNode)(await ReloadAsync(_folder)).Find(_clients.Id)!;
        Assert.Equal(["Acme", "acme-dc01"], clients.Children.Select(c => c.Name));
        Assert.True(_vm.FindItem(_clients.Id)!.IsExpanded);
        Assert.Equal(_dc.Id, _vm.SelectedItem!.Id);
    }

    [Fact]
    public async Task Drop_OntoConnectionReorders()
    {
        await SetUpAsync();

        Assert.True(await _vm.DropAsync(_fs.Id, _dc.Id));

        var acme = (FolderNode)(await ReloadAsync(_folder)).Find(_acme.Id)!;
        Assert.Equal(["acme-fs01", "acme-dc01"], acme.Children.Select(c => c.Name));
    }

    [Fact]
    public async Task Drop_FolderIntoItsOwnSubfolderIsRefused()
    {
        await SetUpAsync();

        Assert.False(_vm.CanDrop(_clients.Id, _acme.Id));
        Assert.False(await _vm.DropAsync(_clients.Id, _acme.Id));
        Assert.Single((await ReloadAsync(_folder)).Roots);
    }

    [Fact]
    public async Task Rebuild_KeepsExpandedFolders()
    {
        await SetUpAsync();
        _vm.FindItem(_clients.Id)!.IsExpanded = true;
        _vm.FindItem(_acme.Id)!.IsExpanded = true;

        await _vm.DuplicateAsync(_vm.FindItem(_fs.Id));

        Assert.True(_vm.FindItem(_clients.Id)!.IsExpanded);
        Assert.True(_vm.FindItem(_acme.Id)!.IsExpanded);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool Answer { get; set; }

        public List<string> Questions { get; } = [];

        public List<string> Errors { get; } = [];

        public bool Confirm(string title, string message)
        {
            Questions.Add(message);
            return Answer;
        }

        public void ShowError(string title, string message) => Errors.Add(message);
    }
}
