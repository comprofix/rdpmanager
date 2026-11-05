using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDPManager.Core.Model;

/// <summary>The kind of a node in the connection tree.</summary>
public enum NodeType
{
    Folder,
    Connection,
}

/// <summary>
/// A folder or connection in the connection tree. <see cref="Settings"/> holds only values set
/// explicitly on this node; anything missing is inherited (see <c>SettingsResolver</c>).
/// </summary>
public abstract class TreeNode
{
    protected TreeNode(string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? NewId() : id;
    }

    /// <summary>Stable identifier. Never changes once created.</summary>
    public string Id { get; }

    public abstract NodeType Type { get; }

    public string Name { get; set; } = string.Empty;

    /// <summary>When the node was last saved (UTC).</summary>
    public DateTimeOffset Modified { get; set; }

    /// <summary>Incremented on every save; reserved for concurrency checks with shared storage.</summary>
    public int Revision { get; set; }

    /// <summary>Explicitly set values only, keyed by setting storage key.</summary>
    public JsonObject Settings { get; set; } = [];

    /// <summary>The folder containing this node, or null for a top-level node.</summary>
    public FolderNode? Parent { get; internal set; }

    /// <summary>Properties this version does not recognise; written back unchanged.</summary>
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }

    /// <summary>Folders containing this node, nearest first.</summary>
    public IEnumerable<FolderNode> Ancestors()
    {
        for (var folder = Parent; folder is not null; folder = folder.Parent)
        {
            yield return folder;
        }
    }

    public static string NewId() => Guid.NewGuid().ToString("D");
}

/// <summary>A folder: holds connections and other folders, and settings they inherit.</summary>
public sealed class FolderNode(string? id = null) : TreeNode(id)
{
    private readonly List<TreeNode> _children = [];

    public override NodeType Type => NodeType.Folder;

    public IReadOnlyList<TreeNode> Children => _children;

    internal void InsertChild(int index, TreeNode child)
    {
        child.Parent = this;
        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
    }

    internal bool RemoveChild(TreeNode child)
    {
        if (!_children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        return true;
    }
}

/// <summary>A connection to a remote system.</summary>
public sealed class ConnectionNode(string? id = null) : TreeNode(id)
{
    public override NodeType Type => NodeType.Connection;
}
