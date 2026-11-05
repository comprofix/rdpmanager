using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RDPManager.App.ViewModels;

/// <summary>The main window: the connection tree, the (empty for now) session area and the status bar.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Action _exit;

    public MainWindowViewModel(ConnectionTreeViewModel tree, string storageFolder, Action exit)
    {
        Tree = tree;
        StorageFolder = storageFolder;
        _exit = exit;
        Tree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionTreeViewModel.SelectedItem))
            {
                OnPropertyChanged(nameof(SourceStatus));
            }
        };
    }

    public ConnectionTreeViewModel Tree { get; }

    /// <summary>The folder holding the connection file (shown so a test run's <c>--config</c> folder is visible).</summary>
    public string StorageFolder { get; }

    /// <summary>Status bar, right: which source the selected item belongs to (DESIGN, Agreed layout).</summary>
    public string SourceStatus => Tree.SelectedItem is null ? string.Empty : $"{Tree.SourceName} · {StorageFolder}";

    [RelayCommand]
    private void Exit() => _exit();
}
