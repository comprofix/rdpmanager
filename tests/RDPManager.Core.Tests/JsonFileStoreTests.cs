using System.Text.Json.Nodes;
using RDPManager.Core.Model;
using RDPManager.Core.Storage;

namespace RDPManager.Core.Tests;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));

    private string FilePath => Path.Combine(_folder, JsonFileStore.FileName);

    private string BackupFolder => Path.Combine(_folder, JsonFileStore.BackupFolderName);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private JsonFileStore NewStore() => new(_folder, _time);

    private void WriteFile(string json)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, json);
    }

    [Fact]
    public async Task MissingFile_LoadsEmptyTreeWithoutCreatingAnything()
    {
        var tree = await NewStore().LoadAsync();

        Assert.Empty(tree.Roots);
        Assert.Equal(ConnectionTree.CurrentSchemaVersion, tree.SchemaVersion);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task SavedNodes_RoundTripThroughTheFile()
    {
        var store = NewStore();
        await store.LoadAsync();
        var clients = new FolderNode { Name = "Clients" };
        clients.Settings["username"] = "svc-admin";
        clients.Settings["passwordStored"] = true;
        var dc = new ConnectionNode { Name = "acme-dc01" };
        dc.Settings["host"] = "acme-dc01.acme.local";

        await store.SaveNodeAsync(clients);
        await store.SaveNodeAsync(dc, clients.Id);

        var reloaded = await NewStore().LoadAsync();
        var folder = Assert.IsType<FolderNode>(Assert.Single(reloaded.Roots));
        var connection = Assert.IsType<ConnectionNode>(Assert.Single(folder.Children));
        Assert.Equal(clients.Id, folder.Id);
        Assert.Equal("Clients", folder.Name);
        Assert.Equal("svc-admin", folder.Settings["username"]!.GetValue<string>());
        Assert.True(folder.Settings["passwordStored"]!.GetValue<bool>());
        Assert.Equal(dc.Id, connection.Id);
        Assert.Equal("acme-dc01.acme.local", connection.Settings["host"]!.GetValue<string>());
    }

    [Fact]
    public async Task Save_UpdatesRevisionAndModified()
    {
        var store = NewStore();
        await store.LoadAsync();
        var dc = new ConnectionNode { Name = "dc" };

        await store.SaveNodeAsync(dc);
        Assert.Equal(1, dc.Revision);
        Assert.Equal(_time.GetUtcNow(), dc.Modified);

        _time.Advance(TimeSpan.FromMinutes(5));
        dc.Name = "dc renamed";
        await store.SaveNodeAsync(dc);

        var reloaded = (await NewStore().LoadAsync()).Find(dc.Id)!;
        Assert.Equal(2, reloaded.Revision);
        Assert.Equal(_time.GetUtcNow(), reloaded.Modified);
        Assert.Equal("dc renamed", reloaded.Name);
    }

    [Fact]
    public async Task Save_WithACopy_UpdatesTheExistingNodeInPlace()
    {
        var store = NewStore();
        var tree = await store.LoadAsync();
        var folder = new FolderNode { Name = "Folder" };
        var dc = new ConnectionNode { Name = "dc" };
        await store.SaveNodeAsync(folder);
        await store.SaveNodeAsync(dc, folder.Id);

        // A settings tab edits a draft copy and saves it.
        var draft = new ConnectionNode(dc.Id) { Name = "dc edited" };
        draft.Settings["port"] = 3390;
        await store.SaveNodeAsync(draft);

        Assert.Same(dc, tree.Find(dc.Id));
        Assert.Same(folder, dc.Parent);
        Assert.Equal("dc edited", dc.Name);
        Assert.Equal(3390, dc.Settings["port"]!.GetValue<int>());
    }

    [Fact]
    public async Task SavingANewFolder_AddsItsContentsAtTheGivenPosition()
    {
        var store = NewStore();
        var tree = await store.LoadAsync();
        var first = new ConnectionNode { Name = "first" };
        var last = new ConnectionNode { Name = "last" };
        await store.SaveNodeAsync(first);
        await store.SaveNodeAsync(last);
        var folder = new FolderNode { Name = "Folder" };
        await store.SaveNodeAsync(folder);
        await store.SaveNodeAsync(new ConnectionNode { Name = "inside" }, folder.Id);

        var copy = tree.CreateDuplicate(folder.Id);
        await store.SaveNodeAsync(copy, parentId: null, index: 1);

        var reloaded = await NewStore().LoadAsync();
        Assert.Equal(["first", "Folder (copy)", "last", "Folder"], reloaded.Roots.Select(n => n.Name));
        var copiedChild = Assert.Single(((FolderNode)reloaded.Roots[1]).Children);
        Assert.Equal("inside", copiedChild.Name);
        Assert.Equal(1, copiedChild.Revision);
    }

    [Fact]
    public async Task DeleteAndMove_ArePersisted()
    {
        var store = NewStore();
        await store.LoadAsync();
        var a = new FolderNode { Name = "A" };
        var b = new FolderNode { Name = "B" };
        var dc = new ConnectionNode { Name = "dc" };
        await store.SaveNodeAsync(a);
        await store.SaveNodeAsync(b);
        await store.SaveNodeAsync(dc, a.Id);

        await store.MoveNodeAsync(dc.Id, b.Id, 0);
        Assert.True(await store.DeleteNodeAsync(a.Id));
        Assert.False(await store.DeleteNodeAsync(a.Id));

        var reloaded = await NewStore().LoadAsync();
        var onlyFolder = Assert.IsType<FolderNode>(Assert.Single(reloaded.Roots));
        Assert.Equal(b.Id, onlyFolder.Id);
        Assert.Equal(dc.Id, Assert.Single(onlyFolder.Children).Id);
    }

    [Fact]
    public async Task UnknownProperties_AreWrittenBackUnchanged()
    {
        WriteFile("""
            {
              "schemaVersion": 1,
              "futureDocumentField": { "a": 1 },
              "defaults": { "port": 3390, "future.setting": "x" },
              "nodes": [
                { "id": "f1", "type": "folder", "name": "F", "colour": "red", "children": [
                  { "id": "c1", "type": "connection", "name": "C", "tags": ["x", "y"], "settings": { "host": "h" } }
                ] }
              ]
            }
            """);
        var store = NewStore();
        await store.LoadAsync();

        await store.SaveNodeAsync(new ConnectionNode { Name = "new" });

        var saved = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal(1, saved["futureDocumentField"]!["a"]!.GetValue<int>());
        Assert.Equal("x", saved["defaults"]!["future.setting"]!.GetValue<string>());
        Assert.Equal("red", saved["nodes"]![0]!["colour"]!.GetValue<string>());
        Assert.Equal("y", saved["nodes"]![0]!["children"]![0]!["tags"]![1]!.GetValue<string>());
    }

    [Fact]
    public async Task NewerFormatVersion_IsRefusedAndFileLeftUnchanged()
    {
        const string json = """{ "schemaVersion": 2, "nodes": [] }""";
        WriteFile(json);

        var ex = await Assert.ThrowsAsync<UnsupportedFileVersionException>(() => NewStore().LoadAsync());

        Assert.Equal(2, ex.FoundVersion);
        Assert.Equal(1, ex.SupportedVersion);
        Assert.Contains("version 2", ex.Message);
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "nodes": [] }""")]
    [InlineData("""{ "schemaVersion": 1, "nodes": [ { "id": "x", "type": "spaceship" } ] }""")]
    [InlineData("""{ "schemaVersion": 1, "nodes": [ { "type": "folder" } ] }""")]
    public async Task UnreadableFile_IsRefusedAndNotOverwritten(string json)
    {
        WriteFile(json);
        var store = NewStore();

        var ex = await Assert.ThrowsAsync<InvalidConnectionFileException>(() => store.LoadAsync());

        Assert.Equal(FilePath, ex.FilePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveNodeAsync(new ConnectionNode()));
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task SuccessfulSave_LeavesNoTempFile()
    {
        var store = NewStore();
        await store.LoadAsync();

        await store.SaveNodeAsync(new ConnectionNode { Name = "dc" });

        Assert.True(File.Exists(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task FailedSave_LeavesTheOriginalIntact()
    {
        var store = NewStore();
        await store.LoadAsync();
        await store.SaveNodeAsync(new ConnectionNode { Name = "original" });
        var before = File.ReadAllText(FilePath);

        // A directory where the temp file should go makes the write fail.
        Directory.CreateDirectory(FilePath + ".tmp");
        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveNodeAsync(new ConnectionNode { Name = "second" }));

        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task FirstLoad_BacksUpTheFileOncePerStore()
    {
        WriteFile("""{ "schemaVersion": 1, "nodes": [] }""");
        var store = NewStore();

        await store.LoadAsync();
        _time.Advance(TimeSpan.FromSeconds(1));
        await store.LoadAsync();

        var backup = Assert.Single(Directory.GetFiles(BackupFolder));
        Assert.Equal("connections-20261105-100000.json", Path.GetFileName(backup));
        Assert.Equal(File.ReadAllText(FilePath), File.ReadAllText(backup));
    }

    [Fact]
    public async Task Backups_KeepTheNewestTen()
    {
        WriteFile("""{ "schemaVersion": 1, "nodes": [] }""");

        for (var i = 0; i < 13; i++)
        {
            await NewStore().LoadAsync();
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var names = Directory.GetFiles(BackupFolder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(JsonFileStore.MaxBackups, names.Count);
        Assert.Equal("connections-20261105-100300.json", names[0]);
        Assert.Equal("connections-20261105-101200.json", names[^1]);
    }

    [Fact]
    public async Task ChangesBeforeLoad_AreRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewStore().SaveNodeAsync(new ConnectionNode()));
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
