using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.App.Services;
using RDPManager.Core.Model;
using RDPManager.Core.Storage;

namespace RDPManager.App.ViewModels;

/// <summary>
/// The connection tree: shows one source's folders and connections and applies tree operations
/// (rename, duplicate, delete, move) immediately through the store (DESIGN: tree operations are immediate).
/// </summary>
public sealed partial class ConnectionTreeViewModel(IConnectionStore store, IDialogService dialogs, string sourceName) : ObservableObject
{
    private const string Title = "RDPManager";

    private ConnectionTree _tree = new();

    public string SourceName { get; } = sourceName;

    /// <summary>Top-level rows: one per source.</summary>
    public ObservableCollection<TreeItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private TreeItemViewModel? _selectedItem;

    /// <summary>Shows a tree that has already been loaded from the store.</summary>
    public void SetTree(ConnectionTree tree)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        Rebuild(selectId: null);
    }

    /// <summary>Finds the row for a node id.</summary>
    public TreeItemViewModel? FindItem(string id) =>
        Items.SelectMany(i => i.SelfAndDescendants()).FirstOrDefault(i => i.Id == id);

    [RelayCommand]
    public void BeginRename(TreeItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item?.Node is null)
        {
            return;
        }

        item.EditName = item.Name;
        item.IsEditing = true;
    }

    public void CancelRename(TreeItemViewModel item)
    {
        item.IsEditing = false;
        item.EditName = item.Name;
    }

    public async Task CommitRenameAsync(TreeItemViewModel item)
    {
        if (!item.IsEditing || item.Node is not { } node)
        {
            return;
        }

        item.IsEditing = false;
        var newName = item.EditName.Trim();
        if (newName.Length == 0)
        {
            dialogs.ShowError(Title, "A name can't be empty. The name was not changed.");
            return;
        }

        if (newName == node.Name)
        {
            return;
        }

        var oldName = node.Name;
        node.Name = newName;
        if (await TrySaveAsync(() => store.SaveNodeAsync(node)))
        {
            item.Name = newName;
        }
        else
        {
            node.Name = oldName;
        }
    }

    [RelayCommand]
    public async Task DuplicateAsync(TreeItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item?.Node is not { } node)
        {
            return;
        }

        var copy = _tree.CreateDuplicate(node.Id);
        if (await TrySaveAsync(() => store.SaveNodeAsync(copy, node.Parent?.Id, _tree.IndexOf(node) + 1)))
        {
            Rebuild(selectId: copy.Id);
        }
    }

    [RelayCommand]
    public async Task DeleteAsync(TreeItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item?.Node is not { } node)
        {
            return;
        }

        if (!dialogs.Confirm(Title, DeleteQuestion(node)))
        {
            return;
        }

        var selectAfter = node.Parent?.Id;
        if (await TrySaveAsync(() => store.DeleteNodeAsync(node.Id)))
        {
            Rebuild(selectId: selectAfter);
        }
    }

    /// <summary>True when <paramref name="draggedId"/> may be dropped on <paramref name="targetId"/> (null: empty space).</summary>
    public bool CanDrop(string draggedId, string? targetId) => _tree.PlanDrop(draggedId, targetId) is not null;

    /// <summary>Moves a dragged node according to the drop rules. Returns false if the drop is not allowed.</summary>
    public async Task<bool> DropAsync(string draggedId, string? targetId)
    {
        var placement = _tree.PlanDrop(draggedId, targetId);
        if (placement is null)
        {
            return false;
        }

        if (!await TrySaveAsync(() => store.MoveNodeAsync(draggedId, placement.ParentId, placement.Index)))
        {
            return false;
        }

        if (placement.ParentId is { } folderId && FindItem(folderId) is { } folder)
        {
            folder.IsExpanded = true;
        }

        Rebuild(selectId: draggedId);
        return true;
    }

    internal static string DeleteQuestion(TreeNode node)
    {
        if (node is FolderNode folder)
        {
            var (folders, connections) = ConnectionTree.CountContents(folder);
            if (folders + connections == 0)
            {
                return $"Delete the empty folder \"{folder.Name}\"?";
            }

            var parts = new List<string>();
            if (folders > 0)
            {
                parts.Add(folders == 1 ? "1 folder" : $"{folders} folders");
            }

            if (connections > 0)
            {
                parts.Add(connections == 1 ? "1 connection" : $"{connections} connections");
            }

            return $"Delete the folder \"{folder.Name}\" and everything in it ({string.Join(" and ", parts)})?\n\nThis can't be undone.";
        }

        return $"Delete the connection \"{node.Name}\"?\n\nThis can't be undone.";
    }

    /// <summary>Runs a store change; on failure shows the error and reloads the tree from the file.</summary>
    private async Task<bool> TrySaveAsync(Func<Task> change)
    {
        try
        {
            await change();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            dialogs.ShowError(Title, $"The change could not be saved.\n\n{ex.Message}");
            try
            {
                _tree = await store.LoadAsync();
                Rebuild(selectId: SelectedItem?.Id);
            }
            catch (Exception reloadError) when (reloadError is IOException or InvalidConnectionFileException)
            {
                dialogs.ShowError(Title, $"The connection file could not be reloaded.\n\n{reloadError.Message}");
            }

            return false;
        }
    }

    /// <summary>Rebuilds the rows from the tree, keeping expanded folders, and selects <paramref name="selectId"/>.</summary>
    private void Rebuild(string? selectId)
    {
        var expanded = Items.SelectMany(i => i.SelfAndDescendants())
            .Where(i => i.IsExpanded && i.Id is not null)
            .Select(i => i.Id!)
            .ToHashSet();

        var source = TreeItemViewModel.ForSource(SourceName);
        foreach (var node in _tree.Roots)
        {
            source.Children.Add(Build(node, source, expanded));
        }

        Items.Clear();
        Items.Add(source);
        SelectedItem = selectId is null ? source : FindItem(selectId) ?? source;
    }

    private static TreeItemViewModel Build(TreeNode node, TreeItemViewModel parent, HashSet<string> expanded)
    {
        var item = TreeItemViewModel.ForNode(node, parent);
        item.IsExpanded = expanded.Contains(node.Id);
        if (node is FolderNode folder)
        {
            foreach (var child in folder.Children)
            {
                item.Children.Add(Build(child, item, expanded));
            }
        }

        return item;
    }
}
