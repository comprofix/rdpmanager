using RDPManager.Core.Model;
using RDPManager.Core.Settings;

namespace RDPManager.Core.Tests;

public class SettingsResolverTests
{
    private readonly ConnectionTree _tree = new();
    private readonly FolderNode _clients = new() { Name = "Clients" };
    private readonly FolderNode _acme = new() { Name = "Acme" };
    private readonly ConnectionNode _dc = new() { Name = "acme-dc01" };
    private readonly SettingsResolver _resolver;

    public SettingsResolverTests()
    {
        _tree.Add(_clients);
        _tree.Add(_acme, _clients.Id);
        _tree.Add(_dc, _acme.Id);
        _resolver = new SettingsResolver(_tree);
    }

    [Fact]
    public void Unset_FallsBackToBuiltInDefault()
    {
        var port = _resolver.Resolve(_dc, "port");

        Assert.Equal(3389, port.Value!.GetValue<int>());
        Assert.Equal(SettingSource.BuiltIn, port.Source);
        Assert.True(port.IsInherited);
    }

    [Fact]
    public void FileDefault_BeatsBuiltIn()
    {
        _tree.Defaults["port"] = 3390;

        var port = _resolver.Resolve(_dc, "port");

        Assert.Equal(3390, port.Value!.GetValue<int>());
        Assert.Equal(SettingSource.FileDefault, port.Source);
    }

    [Fact]
    public void NearestFolder_BeatsFurtherFolderAndFileDefault()
    {
        _tree.Defaults["username"] = "default-user";
        _clients.Settings["username"] = "clients-user";
        _acme.Settings["username"] = "acme-user";

        var username = _resolver.Resolve(_dc, "username");

        Assert.Equal("acme-user", username.Value!.GetValue<string>());
        Assert.Equal(SettingSource.Ancestor, username.Source);
        Assert.Same(_acme, username.SourceNode);
    }

    [Fact]
    public void NodeValue_BeatsEverything()
    {
        _acme.Settings["username"] = "acme-user";
        _dc.Settings["username"] = "dc-user";

        var username = _resolver.Resolve(_dc, "username");

        Assert.Equal("dc-user", username.Value!.GetValue<string>());
        Assert.Equal(SettingSource.Node, username.Source);
        Assert.False(username.IsInherited);
    }

    [Fact]
    public void NonInheritableSetting_IgnoresFoldersAndFileDefaults()
    {
        _acme.Settings["description"] = "folder description";
        _tree.Defaults["description"] = "file description";

        var description = _resolver.Resolve(_dc, "description");

        Assert.Equal("", description.Value!.GetValue<string>());
        Assert.Equal(SettingSource.BuiltIn, description.Source);
    }

    [Fact]
    public void Secret_ResolvesFromStoredMarker()
    {
        _clients.Settings["passwordStored"] = true;

        var password = _resolver.Resolve(_dc, "password");

        Assert.True(password.Value!.GetValue<bool>());
        Assert.Same(_clients, password.SourceNode);
    }

    [Fact]
    public void ResolvedValue_IsACopy()
    {
        _acme.Settings["username"] = "acme-user";

        var username = _resolver.Resolve(_dc, "username");

        Assert.NotSame(_acme.Settings["username"], username.Value);
    }

    [Fact]
    public void ResolveAll_ListsSettingsForTheNodeType()
    {
        var folderKeys = _resolver.ResolveAll(_acme).Select(r => r.Definition.Key).ToList();
        var connectionKeys = _resolver.ResolveAll(_dc).Select(r => r.Definition.Key).ToList();

        Assert.DoesNotContain("host", folderKeys);
        Assert.Contains("host", connectionKeys);
        Assert.Equal(SettingsSchema.All.Count, connectionKeys.Count);
    }

    [Fact]
    public void UnknownKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => _resolver.Resolve(_dc, "nope"));
    }
}
