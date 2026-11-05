namespace RDPManager.App.Sessions;

/// <summary>Details of a disconnect as reported by the RDP control.</summary>
public sealed record DisconnectReport(int Code, int ExtendedCode, string? Description, bool UserInitiated);

/// <summary>
/// A live remote session (the RDP control on Windows). The session tab's screen logic talks to this,
/// so it can be tested without a real connection.
/// </summary>
public interface IRemoteSession
{
    event EventHandler? Connected;

    event EventHandler<DisconnectReport>? Disconnected;

    /// <summary>The session asks to leave full screen (for example its own shortcut).</summary>
    event EventHandler? LeaveFullScreenRequested;

    /// <summary>The session asks its window to minimise.</summary>
    event EventHandler? MinimizeRequested;

    void Connect();

    void Disconnect();

    /// <summary>Tells the control it is (or is no longer) full screen; the app sizes the window itself.</summary>
    void SetFullScreen(bool fullScreen);

    /// <summary>Gives keyboard focus to the session.</summary>
    void Focus();
}
