using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RDPManager.App.Sessions;
using RDPManager.App.ViewModels;

namespace RDPManager.App.Views;

/// <summary>
/// Shows a session in one place: its tab, its undocked window or full screen (<see cref="Placement"/>).
/// The RDP host exists only where the session currently is, so the control is never in two places;
/// it is hidden (not removed) while the Disconnected or Error panel shows.
/// </summary>
public partial class SessionView : UserControl
{
    public static readonly StyledProperty<SessionLocation> PlacementProperty =
        AvaloniaProperty.Register<SessionView, SessionLocation>(nameof(Placement), SessionLocation.Tab);

    private SessionTabViewModel? _session;

    public SessionView()
    {
        InitializeComponent();
        StatusPanel.AddHandler(PointerPressedEvent, (_, _) => _session?.CancelAutoClose(), RoutingStrategies.Tunnel);
    }

    public SessionLocation Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_session is not null)
        {
            _session.PropertyChanged -= OnSessionChanged;
        }

        _session = DataContext as SessionTabViewModel;
        if (_session is not null)
        {
            _session.PropertyChanged += OnSessionChanged;
        }

        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlacementProperty)
        {
            Update();
        }
    }

    private bool _attached;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        HostSlot.Child = null; // Let the host release the native window; the control itself stays alive.
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Update();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var session = _session;
        var here = session is not null && session.Location == Placement && _attached;

        if (here && HostSlot.Child is null)
        {
            HostSlot.Child = new RdpSessionHost();
        }
        else if (!here && HostSlot.Child is not null)
        {
            HostSlot.Child = null;
        }

        if (HostSlot.Child is { } host)
        {
            host.IsVisible = session!.ShowSession;
        }

        ConnectingBar.IsVisible = here && session!.IsConnecting;
        StatusPanel.IsVisible = here && session!.ShowStatusPanel;

        var movedAway = session is not null && Placement == SessionLocation.Tab && session.Location != SessionLocation.Tab;
        MovedPanel.IsVisible = movedAway;
        if (movedAway)
        {
            MovedText.Text = session!.Location == SessionLocation.FullScreen
                ? $"{session.Title} is full screen."
                : $"{session.Title} is open in its own window.";
            BringBackButton.IsVisible = session.Location == SessionLocation.Window;
        }
    }
}
