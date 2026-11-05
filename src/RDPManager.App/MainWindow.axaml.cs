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

        // Drag-and-drop: into a folder, before a connection (reorder), or on empty space (top level).
        Tree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerReleasedEvent, (_, _) => _dragStart = null, RoutingStrategies.Tunnel);
        Tree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        Tree.AddHandler(DragDrop.DropEvent, OnTreeDrop);

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

    private const double DragThreshold = 4;

    private static readonly DataFormat<string> NodeIdFormat = DataFormat.CreateInProcessFormat<string>("RDPManager.NodeId");

    private PointerPressedEventArgs? _dragStart;
    private TreeItemViewModel? _dragItem;
    private bool _dragging;

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Tree).Properties;
        var item = ItemFrom(e.Source);
        if (point.IsRightButtonPressed && item is not null && TreeModel is { } tree)
        {
            tree.SelectedItem = item;
        }

        // Remember a left press on a folder or connection (not the source row, not a rename box) as a possible drag.
        _dragStart = point.IsLeftButtonPressed && item is { Node: not null, IsEditing: false } && e.Source is not TextBox ? e : null;
        _dragItem = _dragStart is null ? null : item;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging || _dragStart is not { } start || _dragItem?.Id is not { } id)
        {
            return;
        }

        var delta = e.GetPosition(Tree) - start.GetPosition(Tree);
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragStart = null;
        _dragging = true;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(NodeIdFormat, id));
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
        }
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TreeModel is { } tree && e.DataTransfer.TryGetValue(NodeIdFormat) is { } id && tree.CanDrop(id, DropTargetId(e))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnTreeDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (TreeModel is { } tree && e.DataTransfer.TryGetValue(NodeIdFormat) is { } id)
        {
            await tree.DropAsync(id, DropTargetId(e));
        }
    }

    /// <summary>The node under the pointer, or null for the source row or empty space (top level).</summary>
    private static string? DropTargetId(DragEventArgs e) => ItemFrom(e.Source)?.Id;

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
