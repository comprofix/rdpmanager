using System.Text.Json.Nodes;
using RDPManager.Core.Model;
using RDPManager.Core.Settings;

namespace RDPManager.Core.Tests;

public class NodeDraftTests
{
    private readonly ConnectionTree _tree = new();
    private readonly FolderNode _clients = new() { Name = "Clients" };
    private readonly FolderNode _acme = new() { Name = "Acme" };
    private readonly ConnectionNode _dc = new() { Name = "acme-dc01" };

    public NodeDraftTests()
    {
        _tree.Add(_clients);
        _tree.Add(_acme, _clients.Id);
        _tree.Add(_dc, _acme.Id);
        _clients.Settings["username"] = "svc-clients";
        _acme.Settings["domain"] = "ACME";
        _dc.Settings["host"] = "dc.acme.test";
    }

    [Fact]
    public void ExistingDraft_StartsClean_AndShowsInheritedSources()
    {
        var draft = NodeDraft.ForExisting(_tree, _dc);

        Assert.False(draft.IsDirty);
        Assert.Equal("acme-dc01", draft.Name);
        var username = draft.Get("username");
        Assert.Equal("svc-clients", username.Value!.GetValue<string>());
        Assert.Same(_clients, username.SourceNode);
        Assert.Same(_acme, draft.Get("domain").SourceNode);
        Assert.Equal(SettingSource.BuiltIn, draft.Get("port").Source);
        Assert.True(draft.IsSetHere("host"));
    }

    [Fact]
    public void SetAndReset_ChangeOnlyTheDraft()
    {
        var draft = NodeDraft.ForExisting(_tree, _dc);

        draft.Set("username", "dc-admin");
        Assert.True(draft.IsDirty);
        Assert.Equal(SettingSource.Node, draft.Get("username").Source);
        Assert.False(_dc.Settings.ContainsKey("username")); // The tree is untouched until Save.

        draft.Reset("username");
        Assert.False(draft.IsDirty);
        Assert.Same(_clients, draft.Get("username").SourceNode);
    }

    [Fact]
    public void Revert_ThrowsAwayChanges()
    {
        var draft = NodeDraft.ForExisting(_tree, _dc);
        draft.Name = "renamed";
        draft.Set("port", 3390);

        draft.Revert();

        Assert.False(draft.IsDirty);
        Assert.Equal("acme-dc01", draft.Name);
        Assert.False(draft.IsSetHere("port"));
    }

    [Fact]
    public void NewConnection_IsDirty_InheritsFromItsFolder_AndNeedsNameAndHost()
    {
        var draft = NodeDraft.ForNew(_tree, NodeType.Connection, _acme.Id);

        Assert.True(draft.IsNew);
        Assert.True(draft.IsDirty);
        Assert.Same(_acme, draft.Parent);
        Assert.Equal("ACME", draft.Get("domain").Value!.GetValue<string>());
        Assert.Null(_tree.Find(draft.Id));
        Assert.Equal(["Name: enter a name.", "Host: enter the computer's name or address."], draft.Validate());

        draft.Name = "acme-web01";
        draft.Set("host", "web.acme.test");
        Assert.Empty(draft.Validate());
    }

    [Fact]
    public void NewFolder_DoesNotNeedAHost()
    {
        var draft = NodeDraft.ForNew(_tree, NodeType.Folder, null);
        draft.Name = "Lab";

        Assert.Empty(draft.Validate());
        Assert.DoesNotContain(draft.Definitions, d => d.Key == "host");
    }

    [Fact]
    public void Validate_ChecksNumberRange()
    {
        var draft = NodeDraft.ForExisting(_tree, _dc);
        draft.Set("port", 70000);

        Assert.Equal(["Port: enter a number from 1 to 65535."], draft.Validate());
    }

    [Fact]
    public void Secrets_CannotBeSetInTheDraft()
    {
        var draft = NodeDraft.ForExisting(_tree, _dc);

        Assert.Throws<InvalidOperationException>(() => draft.Set("password", true));
    }

    [Fact]
    public void ToNode_KeepsIdTrimsValues_AndMarkSavedClearsDirty()
    {
        var draft = NodeDraft.ForExisting(_tree, _dc);
        draft.Name = "  acme-dc02 ";
        draft.Set("host", " dc2.acme.test ");

        var node = draft.ToNode();
        draft.MarkSaved();

        Assert.Equal(_dc.Id, node.Id);
        Assert.Equal("acme-dc02", node.Name);
        Assert.Equal("dc2.acme.test", node.Settings["host"]!.GetValue<string>());
        Assert.False(draft.IsDirty);
        Assert.False(draft.IsNew);
    }

    [Fact]
    public void RenamedElsewhere_FollowsUnlessEditedHere()
    {
        var untouched = NodeDraft.ForExisting(_tree, _dc);
        untouched.OnRenamedElsewhere("from-tree");
        Assert.Equal("from-tree", untouched.Name);
        Assert.False(untouched.IsDirty);

        var edited = NodeDraft.ForExisting(_tree, _dc);
        edited.Name = "typed here";
        edited.OnRenamedElsewhere("from-tree");
        Assert.Equal("typed here", edited.Name);
        Assert.True(edited.IsDirty);
    }

    [Fact]
    public void CountConnectionsUsing_CountsOnlyConnectionsThatInheritFromThisFolder()
    {
        var fs = new ConnectionNode { Name = "acme-fs01" };
        _tree.Add(fs, _acme.Id);
        var globex = new FolderNode { Name = "Globex" };
        _tree.Add(globex, _clients.Id);
        var app = new ConnectionNode { Name = "app" };
        _tree.Add(app, globex.Id);
        globex.Settings["username"] = "globex-user"; // Globex overrides, so its connection doesn't use Clients' value.
        fs.Settings["username"] = "fs-user"; // Overrides itself.

        var draft = NodeDraft.ForExisting(_tree, _clients);

        Assert.Equal(1, draft.CountConnectionsUsing("username")); // Only acme-dc01.
        Assert.Equal(3, draft.CountConnectionsUsing("port"));
        Assert.Equal(0, draft.CountConnectionsUsing("description")); // Not inheritable.
    }
}
