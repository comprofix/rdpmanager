using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.App.Sessions;
using RDPManager.Core.Sessions;
using RoyalApps.Community.Avalonia.Windows.NativeControls;

namespace RDPManager.App.ViewModels;

public enum SessionState
{
    Connecting,
    Connected,
    Disconnected,
    Error,
}

/// <summary>Where a session is shown.</summary>
public enum SessionLocation
{
    Tab,
    Window,
    FullScreen,
}

/// <summary>
/// A session tab (DESIGN: Session behaviour, Agreed layout). Shows Connecting, Connected, Disconnected
/// or Error; a tab whose connection never succeeded closes itself 10 seconds after the error unless the
/// user clicks on it, and closes at once if the credential prompt is cancelled.
/// </summary>
public sealed partial class SessionTabViewModel : TabViewModel, IDisposeWinFormsControl
{
    public const int AutoCloseSeconds = 10;

    private readonly Func<TimeSpan, Action, IDisposable> _startTimer;
    private readonly Action<SessionTabViewModel> _close;
    private readonly Action<SessionTabViewModel> _openSettings;
    private IRemoteSession? _session;
    private IDisposable? _countdown;
    private bool _connectRequested;

    /// <param name="startTimer">Starts a repeating timer on the UI thread; dispose the result to stop it.</param>
    public SessionTabViewModel(
        SessionOptions options,
        Func<TimeSpan, Action, IDisposable> startTimer,
        Action<SessionTabViewModel> close,
        Action<SessionTabViewModel> openSettings)
    {
        Options = options;
        _startTimer = startTimer;
        _close = close;
        _openSettings = openSettings;
        Title = options.Name;
        UpdateDerived();
    }

    public event EventHandler<WinFormsDisposeEventArgs>? DisposeWinFormsControl;

    /// <summary>Asks the window manager to show the session full screen.</summary>
    public event EventHandler? FullScreenRequested;

    /// <summary>Asks the window manager to leave full screen.</summary>
    public event EventHandler? ExitFullScreenRequested;

    /// <summary>Asks the window manager to undock (or redock) the session.</summary>
    public event EventHandler? UndockToggleRequested;

    /// <summary>Asks the window manager to minimise the full-screen window.</summary>
    public event EventHandler? MinimizeRequested;

    public SessionOptions Options { get; }

    public override string Key => KeyFor(Options.ConnectionId);

    public override bool IsSession => true;

    /// <summary>"Undock" while in the tab, "Redock" while in its own window.</summary>
    public string UndockMenuText => Location == SessionLocation.Window ? "Redock" : "Undock";

    public static string KeyFor(string connectionId) => "session:" + connectionId;

    /// <summary>True once the session has connected at least once (auto-close only applies before that).</summary>
    public bool EverConnected { get; private set; }

    [ObservableProperty]
    private SessionState _state = SessionState.Connecting;

    [ObservableProperty]
    private SessionLocation _location = SessionLocation.Tab;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    private bool _showDetails;

    /// <summary>Seconds left before an unsuccessful tab closes itself; null when no countdown is running.</summary>
    [ObservableProperty]
    private int? _closeCountdown;

    public bool IsConnecting => State == SessionState.Connecting;

    public bool IsConnected => State == SessionState.Connected;

    public bool IsDisconnected => State == SessionState.Disconnected;

    public bool IsError => State == SessionState.Error;

    /// <summary>The RDP control is shown while connecting (it draws its own progress) and while connected.</summary>
    public bool ShowSession => State is SessionState.Connecting or SessionState.Connected;

    /// <summary>The Disconnected or Error panel is shown instead of the session.</summary>
    public bool ShowStatusPanel => !ShowSession;

    /// <summary>For example "Closing in 7 s…".</summary>
    public string CountdownText => CloseCountdown is { } s ? $"Closing in {s} s… (click anywhere here to keep it open)" : string.Empty;

    /// <summary>Status bar text for this session (DESIGN: status bar, left).</summary>
    public string StatusLine => State switch
    {
        SessionState.Connecting => $"Connecting · {Options.Host}",
        SessionState.Connected => $"Connected · {Options.Host}",
        SessionState.Disconnected => $"Disconnected · {Options.Host}",
        _ => $"Error · {Options.Host}",
    };

    /// <summary>The heading of the status panel.</summary>
    public string PanelHeading => IsError ? "Couldn't connect" : "Disconnected";

    /// <summary>Called by the session host once the RDP control exists; the first call starts connecting.</summary>
    public void Attach(IRemoteSession session)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        _session = session;
        session.Connected += (_, _) => OnConnected();
        session.Disconnected += (_, report) => OnDisconnected(report);
        session.GoFullScreenRequested += (_, _) => FullScreen();
        session.LeaveFullScreenRequested += (_, _) => ExitFullScreenRequested?.Invoke(this, EventArgs.Empty);
        session.MinimizeRequested += (_, _) => MinimizeRequested?.Invoke(this, EventArgs.Empty);

        if (!_connectRequested)
        {
            _connectRequested = true;
            StartConnect();
        }
    }

    /// <summary>Gives the session keyboard focus (for example when its tab is selected).</summary>
    public void FocusSession()
    {
        if (IsConnected)
        {
            _session?.Focus();
        }
    }

    /// <summary>Tells the RDP control whether it is full screen.</summary>
    public void SetControlFullScreen(bool fullScreen) => _session?.SetFullScreen(fullScreen);

    /// <summary>Stops the auto-close countdown (the user clicked on the panel).</summary>
    public void CancelAutoClose()
    {
        _countdown?.Dispose();
        _countdown = null;
        CloseCountdown = null;
    }

    [RelayCommand]
    private void CancelConnecting()
    {
        if (IsConnecting)
        {
            _session?.Disconnect();
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        if (IsConnected || IsConnecting)
        {
            _session?.Disconnect();
        }
    }

    /// <summary>Reconnect (Disconnected) and Retry (Error).</summary>
    [RelayCommand]
    private void Reconnect()
    {
        if (!IsConnected && !IsConnecting)
        {
            StartConnect();
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        CancelAutoClose();
        _openSettings(this);
    }

    [RelayCommand]
    private void ToggleDetails()
    {
        CancelAutoClose();
        ShowDetails = !ShowDetails;
    }

    [RelayCommand]
    private void FullScreen()
    {
        if (IsConnected && Location != SessionLocation.FullScreen)
        {
            FullScreenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void ToggleUndock() => UndockToggleRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Close() => _close(this);

    /// <summary>Closing a session tab disconnects without asking (DESIGN: Session behaviour).</summary>
    public override Task<bool> PrepareToCloseAsync()
    {
        CancelAutoClose();
        if (IsConnected || IsConnecting)
        {
            try
            {
                _session?.Disconnect();
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }

        return Task.FromResult(true);
    }

    /// <summary>Releases the RDP control for good, after the tab has closed.</summary>
    public void Release()
    {
        CancelAutoClose();
        DisposeWinFormsControl?.Invoke(this, new WinFormsDisposeEventArgs(this));
        _session = null;
    }

    partial void OnStateChanged(SessionState value) => UpdateDerived();

    partial void OnLocationChanged(SessionLocation value) => OnPropertyChanged(nameof(UndockMenuText));

    partial void OnCloseCountdownChanged(int? value) => OnPropertyChanged(nameof(CountdownText));

    private void StartConnect()
    {
        CancelAutoClose();
        ShowDetails = false;
        Message = string.Empty;
        Details = string.Empty;
        State = SessionState.Connecting;
        try
        {
            _session?.Connect();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ShowFailure($"The connection could not be started: {ex.Message}", string.Empty);
        }
    }

    private void OnConnected()
    {
        var first = !EverConnected;
        EverConnected = true;
        CancelAutoClose();
        State = SessionState.Connected;
        _session?.Focus();

        // A connection set to "Full screen" resolution goes full screen when it first connects.
        if (first && Options.Display == DisplayMode.FullScreen)
        {
            FullScreenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDisconnected(DisconnectReport report)
    {
        if (Location == SessionLocation.FullScreen)
        {
            ExitFullScreenRequested?.Invoke(this, EventArgs.Empty);
        }

        var info = DisconnectInfo.Classify(report.Code, report.ExtendedCode, report.Description);

        if (!EverConnected)
        {
            // Never connected. A local, user-initiated end means the user cancelled (including the
            // credential prompt): close straight away. Anything else: show the error, then close.
            if (report.Code == 1 && report.UserInitiated)
            {
                _close(this);
                return;
            }

            ShowFailure(info.Message, info.Details);
            StartCountdown();
            return;
        }

        Message = info.Message;
        Details = info.Details;
        State = info.Kind == DisconnectKind.Error ? SessionState.Error : SessionState.Disconnected;
    }

    private void ShowFailure(string message, string details)
    {
        Message = message;
        Details = details;
        State = SessionState.Error;
    }

    private void StartCountdown()
    {
        CancelAutoClose();
        CloseCountdown = AutoCloseSeconds;
        _countdown = _startTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (CloseCountdown is not { } left)
            {
                return;
            }

            if (left <= 1)
            {
                CancelAutoClose();
                _close(this);
            }
            else
            {
                CloseCountdown = left - 1;
            }
        });
    }

    private void UpdateDerived()
    {
        IsModified = false;
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(ShowSession));
        OnPropertyChanged(nameof(ShowStatusPanel));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(PanelHeading));
        IsInactive = State != SessionState.Connected;
    }
}
