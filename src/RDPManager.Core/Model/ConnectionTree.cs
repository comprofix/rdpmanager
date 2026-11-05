using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDPManager.Core.Model;

/// <summary>Where a dropped node goes: a parent folder (null for the top level) and a position.</summary>
/// <param name="ParentId">The folder to move into, or null for the top level.</param>
/// <param name="Index">Position among the new siblings; <see cref="int.MaxValue"/> means "at the end".</param>
public sealed record DropPlacement(string? ParentId, int Index);

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

    /// <summary>The node's position among its siblings.</summary>
    public int IndexOf(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var siblings = node.Parent?.Children ?? Roots;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (ReferenceEquals(siblings[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Creates a copy of a node (and, for a folder, everything inside it) with new ids, named
    /// "&lt;name&gt; (copy)". It is not added to the tree. Password markers are not copied, because
    /// stored passwords belong to the original node.
    /// </summary>
    public TreeNode CreateDuplicate(string id)
    {
        var original = Find(id) ?? throw new KeyNotFoundException($"No node with id '{id}'.");
        var copy = Copy(original);
        copy.Name = $"{original.Name} (copy)";
        return copy;
    }

    /// <summary>Counts the folders and connections inside a folder, at any depth.</summary>
    public static (int Folders, int Connections) CountContents(FolderNode folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        int folders = 0, connections = 0;
        foreach (var child in folder.Children)
        {
            if (child is FolderNode sub)
            {
                folders++;
                var (f, c) = CountContents(sub);
                folders += f;
                connections += c;
            }
            else
            {
                connections++;
            }
        }

        return (folders, connections);
    }

    /// <summary>
    /// Works out where a dragged node goes when dropped on <paramref name="targetId"/>: into a folder (at the end),
    /// just before a connection (to reorder), or at the end of the top level when the target is null.
    /// Returns null when the drop is not allowed (onto itself, or a folder into itself or one of its subfolders).
    /// </summary>
    public DropPlacement? PlanDrop(string draggedId, string? targetId)
    {
        var dragged = Find(draggedId);
        if (dragged is null)
        {
            return null;
        }

        if (targetId is null)
        {
            return new DropPlacement(null, int.MaxValue);
        }

        var target = Find(targetId);
        if (target is null || ReferenceEquals(target, dragged))
        {
            return null;
        }

        if (target is FolderNode folder)
        {
            return folder.Ancestors().Contains(dragged) ? null : new DropPlacement(folder.Id, int.MaxValue);
        }

        var parent = target.Parent;
        if (parent is not null && (ReferenceEquals(parent, dragged) || parent.Ancestors().Contains(dragged)))
        {
            return null;
        }

        var index = IndexOf(target);
        if (ReferenceEquals(dragged.Parent, parent) && IndexOf(dragged) < index)
        {
            index--; // The dragged node is removed first, which shifts the target up by one.
        }

        return new DropPlacement(parent?.Id, index);
    }

    private static TreeNode Copy(TreeNode original)
    {
        TreeNode copy = original is FolderNode ? new FolderNode() : new ConnectionNode();
        copy.Name = original.Name;
        copy.Settings = (JsonObject)original.Settings.DeepClone();
        foreach (var secret in Settings.SettingsSchema.All.Where(d => d.Type == Settings.SettingType.Secret))
        {
            copy.Settings.Remove(secret.StorageKey);
        }

        copy.UnknownProperties = original.UnknownProperties is null ? null : new(original.UnknownProperties);

        if (original is FolderNode folder && copy is FolderNode folderCopy)
        {
            foreach (var child in folder.Children)
            {
                folderCopy.InsertChild(folderCopy.Children.Count, Copy(child));
            }
        }

        return copy;
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
