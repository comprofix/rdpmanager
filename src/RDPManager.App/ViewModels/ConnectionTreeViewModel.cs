using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.App.Services;
using RDPManager.Core.Credentials;
using RDPManager.Core.Model;
using RDPManager.Core.Settings;
using RDPManager.Core.Storage;

namespace RDPManager.App.ViewModels;

/// <summary>
/// The connection tree: shows one source's folders and connections and applies tree operations
/// (rename, duplicate, delete, move) immediately through the store (DESIGN: tree operations are immediate).
/// </summary>
public sealed partial class ConnectionTreeViewModel(IConnectionStore store, CredentialService credentials, IDialogService dialogs, string sourceName) : ObservableObject
{
    private const string Title = "RDPManager";

    private ConnectionTree _tree = new();

    public string SourceName { get; } = sourceName;

    /// <summary>Top-level rows: one per source.</summary>
    public ObservableCollection<TreeItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private TreeItemViewModel? _selectedItem;

    /// <summary>Raised after the rows were rebuilt (rename, duplicate, delete, move, reload); the view restores keyboard focus.</summary>
    public event EventHandler? Rebuilt;

    /// <summary>Raised after a node was renamed in the tree (id, new name), so an open settings tab can follow.</summary>
    public event EventHandler<(string Id, string Name)>? NodeRenamed;

    /// <summary>Raised after nodes were deleted (the node and everything inside it), so their settings tabs close.</summary>
    public event EventHandler<IReadOnlyCollection<string>>? NodesDeleted;

    /// <summary>The loaded tree; settings drafts resolve inherited values against it.</summary>
    public ConnectionTree Model => _tree;

    /// <summary>Stored passwords (Windows Credential Manager in the app).</summary>
    public CredentialService Credentials { get; } = credentials ?? throw new ArgumentNullException(nameof(credentials));

    /// <summary>Shows a tree that has already been loaded from the store.</summary>
    public void SetTree(ConnectionTree tree)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        Rebuild(selectId: null);
    }

    /// <summary>Finds the row for a node id.</summary>
    public TreeItemViewModel? FindItem(string id) =>
        Items.SelectMany(i => i.SelfAndDescendants()).FirstOrDefault(i => i.Id == id);

    /// <summary>Selects a node in the tree, expanding the folders above it.</summary>
    public void SelectNode(string id)
    {
        if (FindItem(id) is not { } item)
        {
            return;
        }

        for (var parent = item.Parent; parent is not null; parent = parent.Parent)
        {
            parent.IsExpanded = true;
        }

        SelectedItem = item;
        Rebuilt?.Invoke(this, EventArgs.Empty); // Lets the view move focus to the row.
    }

    /// <summary>
    /// Saves a settings draft: adds a new node under its folder, or updates an existing one. Returns false
    /// (after showing the error) if it could not be saved.
    /// </summary>
    public async Task<bool> SaveDraftAsync(NodeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var node = draft.ToNode();
        var existing = draft.IsNew ? null : _tree.Find(draft.Id);
        if (!draft.IsNew && existing is null)
        {
            dialogs.ShowError(Title, $"\"{node.Name}\" no longer exists, so the changes could not be saved.");
            return false;
        }

        var renamed = existing is not null && existing.Name != node.Name;

        // Passwords go to the credential store before the marker is saved; undone if the file save fails.
        SecretChange? secrets = null;
        if (draft.PendingSecrets.Count > 0)
        {
            try
            {
                secrets = Credentials.Begin(node.Id, node.Name, draft.PendingSecrets);
            }
            catch (CredentialStoreException ex)
            {
                dialogs.ShowError(Title, $"The password could not be stored, so nothing was saved.\n\n{ex.Message}");
                return false;
            }
        }

        if (!await TrySaveAsync(() => store.SaveNodeAsync(node, draft.IsNew ? draft.Parent?.Id : null)))
        {
            ReportCredentialFailures(secrets?.Rollback(), "could not be put back as they were");
            return false;
        }

        ReportCredentialFailures(secrets?.Commit(), "could not be removed");

        if (draft.IsNew && draft.Parent is { } parent && FindItem(parent.Id) is { } parentItem)
        {
            parentItem.IsExpanded = true;
        }

        Rebuild(selectId: node.Id);
        if (renamed)
        {
            NodeRenamed?.Invoke(this, (node.Id, node.Name));
        }

        return true;
    }

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
            Rebuild(selectId: node.Id); // The new name may move it (alphabetical order).
            NodeRenamed?.Invoke(this, (node.Id, newName));
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

        var idMap = new Dictionary<string, string>();
        var copy = _tree.CreateDuplicate(node.Id, idMap);

        // The copy gets its own copy of the stored passwords (DESIGN: Credentials, Windows details).
        SecretChange secrets;
        try
        {
            secrets = Credentials.CopyTo(_tree, copy, idMap);
        }
        catch (CredentialStoreException ex)
        {
            dialogs.ShowError(Title, $"The stored passwords could not be copied, so nothing was duplicated.\n\n{ex.Message}");
            return;
        }

        if (await TrySaveAsync(() => store.SaveNodeAsync(copy, node.Parent?.Id)))
        {
            Rebuild(selectId: copy.Id);
        }
        else
        {
            ReportCredentialFailures(secrets.Rollback(), "could not be removed");
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
        var removedIds = item.SelfAndDescendants().Where(i => i.Id is not null).Select(i => i.Id!).ToList();
        if (await TrySaveAsync(() => store.DeleteNodeAsync(node.Id)))
        {
            Rebuild(selectId: selectAfter);
            NodesDeleted?.Invoke(this, removedIds);
            ReportCredentialFailures(Credentials.DeleteFor(removedIds), "could not be removed");
        }
    }

    /// <summary>Tells the user about Credential Manager entries that were left behind.</summary>
    private void ReportCredentialFailures(IReadOnlyList<string>? targets, string what)
    {
        if (targets is { Count: > 0 })
        {
            dialogs.ShowError(
                Title,
                $"These Windows Credential Manager entries {what}:\n\n{string.Join("\n", targets)}\n\n" +
                "You can check them in Control Panel › Credential Manager › Windows Credentials.");
        }
    }

    /// <summary>True when <paramref name="draggedId"/> may be dropped on <paramref name="targetId"/> (null: empty space).</summary>
    public bool CanDrop(string draggedId, string? targetId) => _tree.PlanDrop(draggedId, targetId) is not null;

    /// <summary>
    /// Highlights where a drop would land: the destination folder, or the source row for the top level.
    /// Nothing is highlighted when the drop is not allowed. Returns true when a drop is allowed.
    /// </summary>
    public bool UpdateDropHighlight(string draggedId, string? targetId)
    {
        var placement = _tree.PlanDrop(draggedId, targetId);
        var destination = placement is null
            ? null
            : placement.ParentId is null ? Items.FirstOrDefault() : FindItem(placement.ParentId);

        foreach (var item in Items.SelectMany(i => i.SelfAndDescendants()))
        {
            item.IsDropTarget = ReferenceEquals(item, destination);
        }

        return destination is not null;
    }

    /// <summary>Removes any drop highlight (the drag ended or left the tree).</summary>
    public void ClearDropHighlight()
    {
        foreach (var item in Items.SelectMany(i => i.SelfAndDescendants()))
        {
            item.IsDropTarget = false;
        }
    }

    /// <summary>Moves a dragged node according to the drop rules. Returns false if the drop is not allowed.</summary>
    public async Task<bool> DropAsync(string draggedId, string? targetId)
    {
        var placement = _tree.PlanDrop(draggedId, targetId);
        if (placement is null)
        {
            return false;
        }

        if (!await TrySaveAsync(() => store.MoveNodeAsync(draggedId, placement.ParentId)))
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
        Rebuilt?.Invoke(this, EventArgs.Empty);
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
