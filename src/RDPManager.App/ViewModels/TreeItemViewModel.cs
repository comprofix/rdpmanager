using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RDPManager.Core.Model;

namespace RDPManager.App.ViewModels;

/// <summary>
/// One row in the connection tree: a source (top level, for example "Personal"), a folder or a connection.
/// </summary>
public sealed partial class TreeItemViewModel : ObservableObject
{
    private TreeItemViewModel(TreeNode? node, string name, TreeItemViewModel? parent)
    {
        Node = node;
        _name = name;
        Parent = parent;
    }

    /// <summary>The underlying node; null for a source row.</summary>
    public TreeNode? Node { get; }

    public TreeItemViewModel? Parent { get; }

    public string? Id => Node?.Id;

    public bool IsSource => Node is null;

    public bool IsFolder => Node is FolderNode;

    public bool IsConnection => Node is ConnectionNode;

    public ObservableCollection<TreeItemViewModel> Children { get; } = [];

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    public static TreeItemViewModel ForSource(string name) => new(null, name, null) { IsExpanded = true };

    public static TreeItemViewModel ForNode(TreeNode node, TreeItemViewModel parent) => new(node, node.Name, parent);

    /// <summary>This row and every row below it.</summary>
    public IEnumerable<TreeItemViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(c => c.SelfAndDescendants()))
        {
            yield return descendant;
        }
    }
}
