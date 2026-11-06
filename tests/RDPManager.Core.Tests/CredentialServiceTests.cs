using RDPManager.Core.Credentials;
using RDPManager.Core.Model;

namespace RDPManager.Core.Tests;

public class CredentialServiceTests
{
    private readonly ConnectionTree _tree = new();
    private readonly FolderNode _acme = new() { Name = "Acme" };
    private readonly ConnectionNode _dc = new() { Name = "acme-dc01" };
    private readonly InMemoryCredentialStore _store = new();
    private readonly CredentialService _service;

    public CredentialServiceTests()
    {
        _tree.Add(_acme);
        _tree.Add(_dc, _acme.Id);
        _service = new CredentialService(_store);
    }

    private void Store(TreeNode node, string key, string secret)
    {
        node.Settings[key + "Stored"] = true;
        _store.Write(CredentialService.Target(node.Id, key), secret, node.Name);
    }

    [Fact]
    public void Target_UsesTheNodeIdAndKey()
    {
        Assert.Equal($"RDPManager:{_dc.Id}:gateway.password", CredentialService.Target(_dc.Id, "gateway.password"));
    }

    [Fact]
    public void Resolve_UsesTheNodesOwnPasswordFirst_ThenTheFolders()
    {
        Store(_acme, "password", "folder-pw");
        Assert.Equal("folder-pw", _service.Resolve(_tree, _dc, "password"));

        Store(_dc, "password", "own-pw");
        Assert.Equal("own-pw", _service.Resolve(_tree, _dc, "password"));
    }

    [Fact]
    public void Resolve_IsNullWithoutAMarker_OrWhenTheEntryIsMissing()
    {
        Assert.Null(_service.Resolve(_tree, _dc, "password"));

        _dc.Settings["passwordStored"] = true; // Marker, but nothing in the store.
        Assert.Null(_service.Resolve(_tree, _dc, "password"));
    }

    [Fact]
    public void ForSession_GatewaySharesTheServerPasswordWhenAsked()
    {
        Store(_acme, "password", "server-pw");
        Store(_dc, "gateway.password", "gw-pw");

        Assert.Equal(new SessionSecrets("server-pw", "server-pw"), _service.ForSession(_tree, _dc, gatewaySameCredentials: true));
        Assert.Equal(new SessionSecrets("server-pw", "gw-pw"), _service.ForSession(_tree, _dc, gatewaySameCredentials: false));
    }

    [Fact]
    public void SessionSecrets_NeverShowsPasswordsInText()
    {
        Assert.DoesNotContain("secret", new SessionSecrets("secret", "secret").ToString());
    }

    [Fact]
    public void Begin_WritesNow_AndRemovesOnCommit()
    {
        Store(_dc, "gateway.password", "old-gw");
        var change = _service.Begin(_dc.Id, _dc.Name, new Dictionary<string, string?>
        {
            ["password"] = "new-pw",
            ["gateway.password"] = null,
        });

        Assert.Equal("new-pw", _store.Read(CredentialService.Target(_dc.Id, "password")));
        Assert.Equal("acme-dc01", _store.CommentFor(CredentialService.Target(_dc.Id, "password")));
        Assert.Equal("old-gw", _store.Read(CredentialService.Target(_dc.Id, "gateway.password"))); // Not yet.

        Assert.Empty(change.Commit());
        Assert.Null(_store.Read(CredentialService.Target(_dc.Id, "gateway.password")));
    }

    [Fact]
    public void Rollback_RestoresThePreviousPasswords()
    {
        Store(_dc, "password", "old-pw");
        var change = _service.Begin(_dc.Id, _dc.Name, new Dictionary<string, string?>
        {
            ["password"] = "new-pw",
            ["gateway.password"] = "new-gw",
        });

        Assert.Empty(change.Rollback());

        Assert.Equal("old-pw", _store.Read(CredentialService.Target(_dc.Id, "password")));
        Assert.Null(_store.Read(CredentialService.Target(_dc.Id, "gateway.password")));
    }

    [Fact]
    public void Begin_UndoesItsWritesWhenOneFails()
    {
        _store.FailFor = CredentialService.Target(_dc.Id, "gateway.password");

        Assert.Throws<CredentialStoreException>(() => _service.Begin(_dc.Id, _dc.Name, new Dictionary<string, string?>
        {
            ["password"] = "new-pw",
            ["gateway.password"] = "new-gw",
        }));

        Assert.Empty(_store.Entries);
    }

    [Fact]
    public void CopyTo_CopiesStoredPasswordsAndSetsTheCopysMarkers()
    {
        Store(_acme, "password", "folder-pw");
        Store(_dc, "gateway.password", "gw-pw");
        _dc.Settings["passwordStored"] = true; // Marker without an entry: skipped.
        var map = new Dictionary<string, string>();
        var copy = Assert.IsType<FolderNode>(_tree.CreateDuplicate(_acme.Id, map));
        var dcCopy = Assert.Single(copy.Children);

        var change = _service.CopyTo(_tree, copy, map);

        Assert.Equal("folder-pw", _store.Read(CredentialService.Target(copy.Id, "password")));
        Assert.Equal("gw-pw", _store.Read(CredentialService.Target(dcCopy.Id, "gateway.password")));
        Assert.True(copy.Settings["passwordStored"]!.GetValue<bool>());
        Assert.True(dcCopy.Settings["gateway.passwordStored"]!.GetValue<bool>());
        Assert.False(dcCopy.Settings.ContainsKey("passwordStored"));

        change.Rollback();
        Assert.Null(_store.Read(CredentialService.Target(copy.Id, "password")));
        Assert.Equal("folder-pw", _store.Read(CredentialService.Target(_acme.Id, "password"))); // Original kept.
    }

    [Fact]
    public void DeleteFor_RemovesEveryPasswordOfTheNodes()
    {
        Store(_acme, "password", "a");
        Store(_dc, "password", "b");
        Store(_dc, "gateway.password", "c");

        Assert.Empty(_service.DeleteFor([_acme.Id, _dc.Id]));

        Assert.Empty(_store.Entries);
    }
}
