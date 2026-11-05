using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDPManager.Core.Model;

/// <summary>
/// The whole connection tree of one source: top-level nodes, file-level defaults and any properties
/// this version does not recognise.
/// </summary>
public sealed class ConnectionTree
{
    /// <summary>The newest file format this version reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    private readonly List<TreeNode> _roots = [];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>File-level defaults: the third level of inheritance, after the node and its folders.</summary>
    public JsonObject Defaults { get; set; } = [];

    public IReadOnlyList<TreeNode> Roots => _roots;

    /// <summary>Document properties this version does not recognise; written back unchanged.</summary>
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }

    /// <summary>Every node in the tree, depth first.</summary>
    public IEnumerable<TreeNode> AllNodes()
    {
        var stack = new Stack<TreeNode>(_roots.AsEnumerable().Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            if (node is FolderNode folder)
            {
                for (var i = folder.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push(folder.Children[i]);
                }
            }
        }
    }

    public TreeNode? Find(string id) => AllNodes().FirstOrDefault(n => n.Id == id);

    /// <summary>Adds a node under a folder (or at the top level when <paramref name="parentId"/> is null).</summary>
    public void Add(TreeNode node, string? parentId = null, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (Find(node.Id) is not null)
        {
            throw new InvalidOperationException($"A node with id '{node.Id}' already exists.");
        }

        Insert(node, ResolveFolder(parentId), index);
    }

    /// <summary>Removes a node and everything below it. Returns false if it does not exist.</summary>
    public bool Remove(string id)
    {
        var node = Find(id);
        if (node is null)
        {
            return false;
        }

        Detach(node);
        return true;
    }

    /// <summary>Moves a node to another folder (or the top level) at the given position.</summary>
    public void Move(string id, string? newParentId, int index)
    {
        var node = Find(id) ?? throw new KeyNotFoundException($"No node with id '{id}'.");
        var target = ResolveFolder(newParentId);

        if (target is not null && (ReferenceEquals(target, node) || target.Ancestors().Contains(node)))
        {
            throw new InvalidOperationException("A folder cannot be moved into itself or one of its subfolders.");
        }

        Detach(node);
        Insert(node, target, index);
    }

    private FolderNode? ResolveFolder(string? parentId)
    {
        if (parentId is null)
        {
            return null;
        }

        return Find(parentId) switch
        {
            FolderNode folder => folder,
            null => throw new KeyNotFoundException($"No folder with id '{parentId}'."),
            _ => throw new InvalidOperationException($"Node '{parentId}' is not a folder."),
        };
    }

    private void Insert(TreeNode node, FolderNode? parent, int? index)
    {
        if (parent is null)
        {
            node.Parent = null;
            _roots.Insert(Math.Clamp(index ?? _roots.Count, 0, _roots.Count), node);
        }
        else
        {
            parent.InsertChild(index ?? parent.Children.Count, node);
        }
    }

    private void Detach(TreeNode node)
    {
        if (node.Parent is { } parent)
        {
            parent.RemoveChild(node);
        }
        else
        {
            _roots.Remove(node);
        }
    }
}
