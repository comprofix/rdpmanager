using RDPManager.Core.Model;

namespace RDPManager.Core.Tests;

/// <summary>Tree order agreed with the owner (2026-11-05): folders first, then connections; numbers by value.</summary>
public class NodeOrderTests
{
    private static IEnumerable<string> Sorted(IEnumerable<string> names) =>
        names.Select(n => (TreeNode)new ConnectionNode { Name = n })
            .Order(NodeOrder.Instance)
            .Select(n => n.Name);

    [Fact]
    public void NumbersComeFirstAndCompareByValue()
    {
        string[] expected = ["1-Core", "2-Web", "3-DB", "11-Backup", "12-Archive", "21-Test", "Acme", "Globex", "server1", "server2", "server10"];

        Assert.Equal(expected, Sorted(expected.Reverse()));
        Assert.Equal(expected, Sorted(["server10", "Globex", "12-Archive", "1-Core", "server1", "21-Test", "Acme", "3-DB", "11-Backup", "server2", "2-Web"]));
    }

    [Fact]
    public void ZeroPaddedNumbersSortInNumberOrder()
    {
        Assert.Equal(["01", "02", "03", "10", "11"], Sorted(["11", "03", "10", "01", "02"]));
    }

    [Fact]
    public void FoldersStillComeBeforeConnections()
    {
        var connection = new ConnectionNode { Name = "1-first" };
        var folder = new FolderNode { Name = "zz-folder" };

        Assert.True(NodeOrder.Instance.Compare(folder, connection) < 0);
    }
}
