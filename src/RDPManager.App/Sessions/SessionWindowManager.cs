using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using RDPManager.App.ViewModels;
using RDPManager.App.Views;

namespace RDPManager.App.Sessions;

/// <summary>
/// Moves sessions between their tab, their own window (undock) and full screen (DESIGN: Session behaviour,
/// RDP spike findings). The RDP control is container-handled full screen: the app makes the window full
/// screen and tells the control; leaving full screen and disconnects bring the session back.
/// </summary>
public sealed class SessionWindowManager(Window owner)
{
    private readonly Dictionary<SessionTabViewModel, Window> _undocked = [];
    private readonly Dictionary<SessionTabViewModel, FullScreenState> _fullScreen = [];

    public void Track(SessionTabViewModel session)
    {
        session.FullScreenRequested += (_, _) => EnterFullScreen(session);
        session.ExitFullScreenRequested += (_, _) => Dispatcher.UIThread.Post(() => ExitFullScreen(session));
        session.UndockToggleRequested += (_, _) => ToggleUndock(session);
        session.MinimizeRequested += (_, _) =>
        {
            if (_fullScreen.TryGetValue(session, out var fs))
            {
                fs.Window.WindowState = WindowState.Minimized;
            }
        };
    }

    /// <summary>The session's tab closed: close its windows without bringing it back.</summary>
    public void Forget(SessionTabViewModel session)
    {
        if (_fullScreen.Remove(session, out var fs))
        {
            fs.Bar.Stop();
            fs.Window.Close();
        }

        if (_undocked.Remove(session, out var window))
        {
            window.Close();
        }
    }

    private void ToggleUndock(SessionTabViewModel session)
    {
        switch (session.Location)
        {
            case SessionLocation.Tab:
                Undock(session);
                break;
            case SessionLocation.Window when _undocked.TryGetValue(session, out var window):
                window.Close(); // Closing redocks.
                break;
        }
    }

    private void Undock(SessionTabViewModel session)
    {
        var window = new Window
        {
            Title = $"{session.Title} - RDPManager",
            Width = 1024,
            Height = 768,
            Content = new SessionView { Placement = SessionLocation.Window, DataContext = session },
        };
        window.Closing += (_, _) =>
        {
            if (_undocked.Remove(session) && session.Location == SessionLocation.Window)
            {
                session.Location = SessionLocation.Tab;
            }
        };

        _undocked[session] = window;
        session.Location = SessionLocation.Window;
        window.Show(owner);
    }

    private void EnterFullScreen(SessionTabViewModel session)
    {
        if (session.Location == SessionLocation.FullScreen)
        {
            return;
        }

        var previous = session.Location;
        Window source = previous == SessionLocation.Window && _undocked.TryGetValue(session, out var undocked) ? undocked : owner;
        var bounds = (source.Screens.ScreenFromWindow(source) ?? source.Screens.Primary)?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

        var window = new Window
        {
            Title = $"{session.Title} - RDPManager",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = bounds.Position,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            Content = new SessionView { Placement = SessionLocation.FullScreen, DataContext = session },
        };

        var bar = new FullScreenBar(
            session.Title,
            bounds,
            exitFullScreen: () => ExitFullScreen(session),
            minimise: () => window.WindowState = WindowState.Minimized,
            disconnect: () => session.DisconnectCommand.Execute(null),
            close: () => session.CloseCommand.Execute(null));

        window.Closing += (_, _) => ExitFullScreen(session);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                bar.Paused = window.WindowState == WindowState.Minimized;
                if (window.WindowState == WindowState.Normal && _fullScreen.ContainsKey(session))
                {
                    window.WindowState = WindowState.FullScreen; // Restored from the taskbar.
                }
            }
        };

        _fullScreen[session] = new FullScreenState(window, bar, previous);
        session.Location = SessionLocation.FullScreen;
        window.Show();
        window.WindowState = WindowState.FullScreen;

        Dispatcher.UIThread.Post(() =>
        {
            session.SetControlFullScreen(true);
            session.FocusSession();
            bar.Start();
        }, DispatcherPriority.Background);
    }

    private void ExitFullScreen(SessionTabViewModel session)
    {
        if (!_fullScreen.Remove(session, out var fs))
        {
            return;
        }

        fs.Bar.Stop();
        session.SetControlFullScreen(false);
        session.Location = fs.Previous == SessionLocation.Window && _undocked.ContainsKey(session)
            ? SessionLocation.Window
            : SessionLocation.Tab;
        fs.Window.Close();
        Dispatcher.UIThread.Post(session.FocusSession, DispatcherPriority.Background);
    }

    private sealed record FullScreenState(Window Window, FullScreenBar Bar, SessionLocation Previous);
}
