using RoyalApps.Community.Rdp.WinForms;

namespace RDPManager.App.Sessions;

/// <summary>An <see cref="IRemoteSession"/> backed by the Royal Apps RDP control (Microsoft's RDP ActiveX control).</summary>
internal sealed class RdpRemoteSession : IRemoteSession
{
    /// <summary>mstsc's default: Windows key combinations go to the remote session only in full screen.</summary>
    private const int KeyboardHookFullScreenOnly = 2;

    private readonly RdpControl _control;

    public RdpRemoteSession(RdpControl control)
    {
        _control = control;
        control.OnConnected += (_, _) => Connected?.Invoke(this, EventArgs.Empty);
        control.OnDisconnected += (_, e) => Disconnected?.Invoke(
            this,
            new DisconnectReport(Convert.ToInt32(e.DisconnectCode), Convert.ToInt32(e.ExtendedDisconnectReasonCode), e.Description, e.UserInitiated));
        control.OnRequestLeaveFullScreen += (_, _) => LeaveFullScreenRequested?.Invoke(this, EventArgs.Empty);
        control.OnRequestContainerMinimize += (_, _) => MinimizeRequested?.Invoke(this, EventArgs.Empty);

        // The wrapper's keyboard setting maps only to "local" (0) or "always remote" (1); set mstsc's
        // default directly once the wrapper has configured the client (RDP spike finding).
        control.RdpClientConfigured += (_, _) =>
        {
            if (control.RdpClient is { } client)
            {
                client.KeyboardHookMode = KeyboardHookFullScreenOnly;
                SubscribeGoFullScreen(client);
            }
        };
    }

    /// <summary>
    /// In container-handled full screen, Microsoft's control raises OnRequestGoFullScreen for Ctrl+Alt+Break.
    /// The wrapper does not forward it, so subscribe on the underlying ActiveX client by name.
    /// </summary>
    private void SubscribeGoFullScreen(object client)
    {
        if (_goFullScreenSubscribed)
        {
            return;
        }

        var eventInfo = client.GetType().GetEvent("OnRequestGoFullScreen");
        if (eventInfo?.EventHandlerType is not { } handlerType)
        {
            return;
        }

        var raise = GetType().GetMethod(nameof(OnRequestGoFullScreen), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        try
        {
            eventInfo.AddEventHandler(client, Delegate.CreateDelegate(handlerType, this, raise));
            _goFullScreenSubscribed = true;
        }
        catch (ArgumentException)
        {
            // Unexpected delegate shape: Ctrl+Alt+Break simply won't go full screen.
        }
    }

    private bool _goFullScreenSubscribed;

    private void OnRequestGoFullScreen(object? sender, EventArgs e) => GoFullScreenRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? GoFullScreenRequested;

    public event EventHandler? Connected;

    public event EventHandler<DisconnectReport>? Disconnected;

    public event EventHandler? LeaveFullScreenRequested;

    public event EventHandler? MinimizeRequested;

    public void Connect() => _control.Connect();

    public void Disconnect() => _control.Disconnect();

    public void SetFullScreen(bool fullScreen)
    {
        if (_control.RdpClient is { } client && client.FullScreen != fullScreen)
        {
            client.FullScreen = fullScreen;
        }
    }

    public void Focus() => _control.FocusRdpClient();
}
