using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.App.Services;
using RDPManager.Core.Model;
using RDPManager.Core.Sessions;
using RDPManager.Core.Settings;

namespace RDPManager.App.ViewModels;

/// <summary>The main window: the connection tree, the tab strip and the status bar.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Func<ProgramSettingsTabViewModel> _createProgramSettingsTab;
    private readonly Func<TimeSpan, Action, IDisposable> _startTimer;
    private readonly Action _exit;

    /// <param name="startTimer">Starts a repeating UI-thread timer (used by session auto-close).</param>
    public MainWindowViewModel(
        ConnectionTreeViewModel tree,
        IDialogService dialogs,
        string storageFolder,
        Func<ProgramSettingsTabViewModel> createProgramSettingsTab,
        Func<TimeSpan, Action, IDisposable> startTimer,
        Action exit)
    {
        Tree = tree;
        _dialogs = dialogs;
        StorageFolder = storageFolder;
        _createProgramSettingsTab = createProgramSettingsTab;
        _startTimer = startTimer;
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

    /// <summary>Raised when a session tab is opened, so the view can manage its full-screen and undocked windows.</summary>
    public event EventHandler<SessionTabViewModel>? SessionOpened;

    /// <summary>Raised after a session tab closed, so the view can close any of its windows.</summary>
    public event EventHandler<SessionTabViewModel>? SessionClosed;

    /// <summary>Status bar, left: the selected session's state (DESIGN, Agreed layout).</summary>
    public string SessionStatus => SelectedTab is SessionTabViewModel session ? session.StatusLine : string.Empty;

    partial void OnSelectedTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        if (oldValue is SessionTabViewModel oldSession)
        {
            oldSession.PropertyChanged -= OnSelectedSessionChanged;
        }

        if (newValue is SessionTabViewModel newSession)
        {
            newSession.PropertyChanged += OnSelectedSessionChanged;
        }

        OnPropertyChanged(nameof(SessionStatus));
    }

    private void OnSelectedSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionTabViewModel.StatusLine))
        {
            OnPropertyChanged(nameof(SessionStatus));
        }
    }

    /// <summary>
    /// Connects to a connection (right-click › Connect, Enter, double-click). If it already has a session
    /// tab, switches to it instead of opening a second session (DESIGN: Session behaviour).
    /// </summary>
    [RelayCommand]
    public void Connect(TreeItemViewModel? item)
    {
        item ??= Tree.SelectedItem;
        if (item?.Node is not ConnectionNode connection)
        {
            return;
        }

        if (FindTab<SessionTabViewModel>(SessionTabViewModel.KeyFor(connection.Id)) is { } existing)
        {
            SelectedTab = existing;
            return;
        }

        var options = SessionOptions.For(Tree.Model, connection);
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            _dialogs.ShowError("RDPManager", $"\"{connection.Name}\" has no host. Open its settings and enter the computer's name or address.");
            return;
        }

        var session = new SessionTabViewModel(options, _startTimer, CloseSession, OpenSettingsFor);
        AddTab(session);
        SessionOpened?.Invoke(this, session);
    }

    /// <summary>View › Full screen: the selected session.</summary>
    [RelayCommand]
    public void FullScreen()
    {
        if (SelectedTab is SessionTabViewModel session)
        {
            session.FullScreenCommand.Execute(null);
        }
    }

    private void CloseSession(SessionTabViewModel session) => _ = CloseTabAsync(session);

    private void OpenSettingsFor(SessionTabViewModel session)
    {
        if (Tree.FindItem(session.Options.ConnectionId) is { } item)
        {
            OpenSettings(item);
        }
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
            if (tab is SessionTabViewModel session)
            {
                SessionClosed?.Invoke(this, session);
                session.Release();
            }
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
