using RDPManager.Core.Model;

namespace RDPManager.Core.Tests;

public class ConnectionTreeTests
{
    private static (ConnectionTree Tree, FolderNode Clients, FolderNode Acme, ConnectionNode Dc) Sample()
    {
        var tree = new ConnectionTree();
        var clients = new FolderNode { Name = "Clients" };
        var acme = new FolderNode { Name = "Acme" };
        var dc = new ConnectionNode { Name = "acme-dc01" };
        tree.Add(clients);
        tree.Add(acme, clients.Id);
        tree.Add(dc, acme.Id);
        return (tree, clients, acme, dc);
    }

    [Fact]
    public void NewNodes_GetDistinctIds()
    {
        Assert.NotEqual(new ConnectionNode().Id, new ConnectionNode().Id);
    }

    [Fact]
    public void Add_PlacesNodesUnderParents()
    {
        var (tree, clients, acme, dc) = Sample();

        Assert.Same(clients, Assert.Single(tree.Roots));
        Assert.Same(acme, Assert.Single(clients.Children));
        Assert.Same(acme, dc.Parent);
        Assert.Equal([acme, clients], dc.Ancestors());
        Assert.Equal(3, tree.AllNodes().Count());
    }

    [Fact]
    public void Add_RejectsDuplicateIdAndNonFolderParent()
    {
        var (tree, _, _, dc) = Sample();

        Assert.Throws<InvalidOperationException>(() => tree.Add(new ConnectionNode(dc.Id)));
        Assert.Throws<InvalidOperationException>(() => tree.Add(new ConnectionNode(), dc.Id));
        Assert.Throws<KeyNotFoundException>(() => tree.Add(new ConnectionNode(), "missing"));
    }

    [Fact]
    public void Remove_RemovesSubtree()
    {
        var (tree, clients, acme, dc) = Sample();

        Assert.True(tree.Remove(acme.Id));

        Assert.Empty(clients.Children);
        Assert.Null(tree.Find(dc.Id));
        Assert.False(tree.Remove(acme.Id));
    }

    [Fact]
    public void Move_ChangesParentAndPosition()
    {
        var (tree, clients, acme, dc) = Sample();
        var other = new ConnectionNode { Name = "acme-fs01" };
        tree.Add(other, acme.Id);

        tree.Move(dc.Id, null, 0);
        Assert.Same(dc, tree.Roots[0]);
        Assert.Null(dc.Parent);

        tree.Move(dc.Id, acme.Id, 0);
        Assert.Equal([dc, other], acme.Children);
        Assert.Same(acme, dc.Parent);
        Assert.Same(clients, Assert.Single(tree.Roots));
    }

    [Fact]
    public void CreateDuplicate_CopiesSubtreeWithNewIdsAndWithoutPasswordMarkers()
    {
        var (tree, clients, acme, dc) = Sample();
        acme.Settings["username"] = "svc";
        acme.Settings["passwordStored"] = true;
        dc.Settings["gateway.passwordStored"] = true;
        dc.Settings["host"] = "dc.acme.local";

        var copy = Assert.IsType<FolderNode>(tree.CreateDuplicate(acme.Id));

        Assert.Equal("Acme (copy)", copy.Name);
        Assert.NotEqual(acme.Id, copy.Id);
        Assert.Null(copy.Parent);
        Assert.Equal("svc", copy.Settings["username"]!.GetValue<string>());
        Assert.False(copy.Settings.ContainsKey("passwordStored"));
        var childCopy = Assert.IsType<ConnectionNode>(Assert.Single(copy.Children));
        Assert.Equal("acme-dc01", childCopy.Name);
        Assert.NotEqual(dc.Id, childCopy.Id);
        Assert.Equal("dc.acme.local", childCopy.Settings["host"]!.GetValue<string>());
        Assert.False(childCopy.Settings.ContainsKey("gateway.passwordStored"));
        Assert.True(acme.Settings.ContainsKey("passwordStored")); // The original is untouched.
        Assert.Same(acme, Assert.Single(clients.Children));
    }

    [Fact]
    public void CountContents_CountsAtAnyDepth()
    {
        var (tree, clients, acme, _) = Sample();
        tree.Add(new ConnectionNode(), acme.Id);
        tree.Add(new FolderNode(), acme.Id);

        Assert.Equal((2, 2), ConnectionTree.CountContents(clients));
    }

    [Fact]
    public void PlanDrop_OntoFolder_MovesIntoItAtTheEnd()
    {
        var (tree, clients, _, dc) = Sample();

        Assert.Equal(new DropPlacement(clients.Id, int.MaxValue), tree.PlanDrop(dc.Id, clients.Id));
    }

    [Fact]
    public void PlanDrop_OntoConnection_MovesBeforeIt()
    {
        var (tree, _, acme, dc) = Sample();
        var fs = new ConnectionNode { Name = "fs" };
        var web = new ConnectionNode { Name = "web" };
        tree.Add(fs, acme.Id);
        tree.Add(web, acme.Id); // acme: dc(0), fs(1), web(2)

        // Moving down: dc onto web lands between fs and web.
        var down = tree.PlanDrop(dc.Id, web.Id)!;
        tree.Move(dc.Id, down.ParentId, down.Index);
        Assert.Equal([fs, dc, web], acme.Children);

        // Moving up: web onto fs lands first.
        var up = tree.PlanDrop(web.Id, fs.Id)!;
        tree.Move(web.Id, up.ParentId, up.Index);
        Assert.Equal([web, fs, dc], acme.Children);
    }

    [Fact]
    public void PlanDrop_OnEmptySpace_MovesToTopLevelEnd()
    {
        var (tree, clients, _, dc) = Sample();

        var placement = tree.PlanDrop(dc.Id, null)!;
        tree.Move(dc.Id, placement.ParentId, placement.Index);

        Assert.Equal([clients, dc], tree.Roots);
    }

    [Fact]
    public void PlanDrop_RejectsDropsIntoItself()
    {
        var (tree, clients, acme, dc) = Sample();

        Assert.Null(tree.PlanDrop(acme.Id, acme.Id));
        Assert.Null(tree.PlanDrop(clients.Id, acme.Id));
        Assert.Null(tree.PlanDrop(clients.Id, dc.Id));
        Assert.Null(tree.PlanDrop("missing", acme.Id));
    }

    [Fact]
    public void Move_RejectsMovingFolderIntoItselfOrDescendant()
    {
        var (tree, clients, acme, _) = Sample();

        Assert.Throws<InvalidOperationException>(() => tree.Move(clients.Id, clients.Id, 0));
        Assert.Throws<InvalidOperationException>(() => tree.Move(clients.Id, acme.Id, 0));
    }
}
