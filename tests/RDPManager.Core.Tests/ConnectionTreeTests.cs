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
    public void Move_RejectsMovingFolderIntoItselfOrDescendant()
    {
        var (tree, clients, acme, _) = Sample();

        Assert.Throws<InvalidOperationException>(() => tree.Move(clients.Id, clients.Id, 0));
        Assert.Throws<InvalidOperationException>(() => tree.Move(clients.Id, acme.Id, 0));
    }
}
