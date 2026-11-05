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
    public void Move_ChangesParentAndKeepsOrder()
    {
        var (tree, clients, acme, dc) = Sample();
        var other = new ConnectionNode { Name = "acme-fs01" };
        tree.Add(other, acme.Id);

        tree.Move(dc.Id, null);
        Assert.Equal([clients, dc], tree.Roots); // Folder first, then connection.
        Assert.Null(dc.Parent);

        tree.Move(dc.Id, acme.Id);
        Assert.Equal([dc, other], acme.Children); // Alphabetical.
        Assert.Same(acme, dc.Parent);
        Assert.Same(clients, Assert.Single(tree.Roots));
    }

    [Fact]
    public void Children_AreFoldersFirstThenConnections_EachAlphabetical()
    {
        var tree = new ConnectionTree();
        var parent = new FolderNode { Name = "Parent" };
        tree.Add(parent);
        foreach (var name in new[] { "zeta", "Alpha", "server10", "server2" })
        {
            tree.Add(new ConnectionNode { Name = name }, parent.Id);
        }

        foreach (var name in new[] { "Zulu", "bravo" })
        {
            tree.Add(new FolderNode { Name = name }, parent.Id);
        }

        Assert.Equal(["bravo", "Zulu", "Alpha", "server2", "server10", "zeta"], parent.Children.Select(c => c.Name));
    }

    [Fact]
    public void Reorder_MovesARenamedNodeToItsPlace()
    {
        var (tree, _, acme, dc) = Sample();
        var fs = new ConnectionNode { Name = "acme-fs01" };
        tree.Add(fs, acme.Id);

        dc.Name = "zz-dc";
        tree.Reorder(dc);

        Assert.Equal([fs, dc], acme.Children);
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
    public void PlanDrop_OntoFolder_MovesIntoIt()
    {
        var (tree, clients, _, dc) = Sample();

        Assert.Equal(new DropPlacement(clients.Id), tree.PlanDrop(dc.Id, clients.Id));
    }

    [Fact]
    public void PlanDrop_OntoConnection_MovesIntoItsFolder()
    {
        var (tree, clients, acme, _) = Sample();
        var lab = new ConnectionNode { Name = "lab-01" };
        tree.Add(lab);

        Assert.Equal(new DropPlacement(acme.Id), tree.PlanDrop(lab.Id, acme.Id)); // A folder: into it.
        var target = new ConnectionNode { Name = "other" };
        tree.Add(target, clients.Id);
        Assert.Equal(new DropPlacement(clients.Id), tree.PlanDrop(lab.Id, target.Id));
    }

    [Fact]
    public void PlanDrop_FolderLandsAboveConnections()
    {
        // The owner's case: moving "Firewalls" into a folder that holds a connection puts it first.
        var (tree, clients, acme, _) = Sample();
        var globex = new FolderNode { Name = "Globex" };
        tree.Add(globex, clients.Id);
        var app = new ConnectionNode { Name = "globex-app01" };
        tree.Add(app, globex.Id);
        var firewalls = new FolderNode { Name = "Firewalls" };
        tree.Add(firewalls, acme.Id);

        var placement = tree.PlanDrop(firewalls.Id, globex.Id)!;
        tree.Move(firewalls.Id, placement.ParentId);

        Assert.Equal([firewalls, app], globex.Children);
    }

    [Fact]
    public void PlanDrop_OnEmptySpace_MovesToTopLevel()
    {
        var (tree, clients, _, dc) = Sample();

        var placement = tree.PlanDrop(dc.Id, null)!;
        tree.Move(dc.Id, placement.ParentId);

        Assert.Equal([clients, dc], tree.Roots);
    }

    [Fact]
    public void PlanDrop_RejectsDropsIntoItselfAndDropsThatChangeNothing()
    {
        var (tree, clients, acme, dc) = Sample();
        var fs = new ConnectionNode { Name = "fs" };
        tree.Add(fs, acme.Id);

        Assert.Null(tree.PlanDrop(acme.Id, acme.Id));
        Assert.Null(tree.PlanDrop(clients.Id, acme.Id));
        Assert.Null(tree.PlanDrop(clients.Id, dc.Id));
        Assert.Null(tree.PlanDrop("missing", acme.Id));
        Assert.Null(tree.PlanDrop(dc.Id, fs.Id)); // Already in that folder.
        Assert.Null(tree.PlanDrop(clients.Id, null)); // Already at the top level.
    }

    [Fact]
    public void Move_RejectsMovingFolderIntoItselfOrDescendant()
    {
        var (tree, clients, acme, _) = Sample();

        Assert.Throws<InvalidOperationException>(() => tree.Move(clients.Id, clients.Id));
        Assert.Throws<InvalidOperationException>(() => tree.Move(clients.Id, acme.Id));
    }
}
