using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RDPManager.App.ViewModels;

namespace RDPManager.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Right-click selects the row under the pointer, so the context menu acts on it.
        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        Tree.KeyDown += OnTreeKeyDown;

        // In-place rename: focus the box when it appears; Enter or leaving it saves, Esc cancels.
        AddHandler(KeyDownEvent, OnRenameKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, OnRenameLostFocus, RoutingStrategies.Bubble);
    }

    static MainWindow()
    {
        // When a rename box becomes visible (F2 or the context menu), focus it and select the name.
        IsVisibleProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            if (box.IsVisible && box.Classes.Contains("rename"))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    box.Focus();
                    box.SelectAll();
                }, DispatcherPriority.Loaded);
            }
        });
    }

    private ConnectionTreeViewModel? TreeModel => (DataContext as MainWindowViewModel)?.Tree;

    private static TreeItemViewModel? ItemFrom(object? source) =>
        (source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as TreeItemViewModel;

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Tree).Properties.IsRightButtonPressed && ItemFrom(e.Source) is { } item && TreeModel is { } tree)
        {
            tree.SelectedItem = item;
        }
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (TreeModel is not { } tree || tree.SelectedItem is not { IsEditing: false } item)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F2:
                tree.BeginRename(item);
                e.Handled = true;
                break;
            case Key.Delete:
                _ = tree.DeleteAsync(item);
                e.Handled = true;
                break;
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox box || !box.Classes.Contains("rename") || box.DataContext is not TreeItemViewModel item || TreeModel is not { } tree)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = tree.CommitRenameAsync(item);
            Tree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tree.CancelRename(item);
            Tree.Focus();
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox box && box.Classes.Contains("rename") && box.DataContext is TreeItemViewModel { IsEditing: true } item && TreeModel is { } tree)
        {
            _ = tree.CommitRenameAsync(item);
        }
    }
}
