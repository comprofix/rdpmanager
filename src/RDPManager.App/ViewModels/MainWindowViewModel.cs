using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.App.Services;
using RDPManager.Core.Model;
using RDPManager.Core.Settings;

namespace RDPManager.App.ViewModels;

/// <summary>The main window: the connection tree, the tab strip and the status bar.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Func<ProgramSettingsTabViewModel> _createProgramSettingsTab;
    private readonly Action _exit;

    public MainWindowViewModel(
        ConnectionTreeViewModel tree,
        IDialogService dialogs,
        string storageFolder,
        Func<ProgramSettingsTabViewModel> createProgramSettingsTab,
        Action exit)
    {
        Tree = tree;
        _dialogs = dialogs;
        StorageFolder = storageFolder;
        _createProgramSettingsTab = createProgramSettingsTab;
        _exit = exit;

        Tree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionTreeViewModel.SelectedItem))
            {
                OnPropertyChanged(nameof(SourceStatus));
            }
        };
        Tree.NodeRenamed += (_, renamed) => FindTab<SettingsTabViewModel>(SettingsTabViewModel.KeyFor(renamed.Id))?.OnRenamedElsewhere(renamed.Name);
        Tree.NodesDeleted += (_, ids) =>
        {
            foreach (var id in ids)
            {
                if (FindTab<SettingsTabViewModel>(SettingsTabViewModel.KeyFor(id)) is { } tab)
                {
                    Tabs.Remove(tab); // The item is gone; there is nothing left to save.
                }
            }
        };
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabs));
    }

    public ConnectionTreeViewModel Tree { get; }

    /// <summary>The folder holding the connection file (shown so a test run's <c>--config</c> folder is visible).</summary>
    public string StorageFolder { get; }

    /// <summary>Status bar, right: which source the selected item belongs to (DESIGN, Agreed layout).</summary>
    public string SourceStatus => Tree.SelectedItem is null ? string.Empty : $"{Tree.SourceName} · {StorageFolder}";

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private TabViewModel? _selectedTab;

    public bool HasTabs => Tabs.Count > 0;

    /// <summary>Opens the settings tab for a folder or connection (right-click › Settings).</summary>
    [RelayCommand]
    public void OpenSettings(TreeItemViewModel? item)
    {
        item ??= Tree.SelectedItem;
        if (item?.Node is not { } node)
        {
            return;
        }

        var existing = FindTab<TabViewModel>(SettingsTabViewModel.KeyFor(node.Id));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        AddTab(new SettingsTabViewModel(NodeDraft.ForExisting(Tree.Model, node), Tree, _dialogs, CloseWithoutPrompt));
    }

    /// <summary>Opens an empty settings tab for a new connection in the selected folder. Nothing is created until Save.</summary>
    [RelayCommand]
    public void NewConnection(TreeItemViewModel? item) => OpenNew(NodeType.Connection, item);

    /// <summary>Opens an empty settings tab for a new folder in the selected folder. Nothing is created until Save.</summary>
    [RelayCommand]
    public void NewFolder(TreeItemViewModel? item) => OpenNew(NodeType.Folder, item);

    /// <summary>File › Settings…</summary>
    [RelayCommand]
    public void OpenProgramSettings()
    {
        var existing = FindTab<TabViewModel>(ProgramSettingsTabViewModel.TabKey);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        AddTab(_createProgramSettingsTab());
    }

    /// <summary>Closes a tab, asking about unsaved changes first.</summary>
    [RelayCommand]
    public async Task CloseTabAsync(TabViewModel? tab)
    {
        if (tab is not null && Tabs.Contains(tab) && await tab.PrepareToCloseAsync())
        {
            Tabs.Remove(tab);
        }
    }

    [RelayCommand]
    private void Exit() => _exit();

    private void OpenNew(NodeType type, TreeItemViewModel? item)
    {
        item ??= Tree.SelectedItem;

        // Inside the selected folder; next to the selected connection; at the top level for the source row.
        var parentId = item?.Node switch
        {
            FolderNode folder => folder.Id,
            ConnectionNode connection => connection.Parent?.Id,
            _ => null,
        };

        AddTab(new SettingsTabViewModel(NodeDraft.ForNew(Tree.Model, type, parentId), Tree, _dialogs, CloseWithoutPrompt));
    }

    private void AddTab(TabViewModel tab)
    {
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void CloseWithoutPrompt(SettingsTabViewModel tab) => Tabs.Remove(tab);

    private T? FindTab<T>(string key)
        where T : TabViewModel => Tabs.OfType<T>().FirstOrDefault(t => t.Key == key);
}
