using RDPManager.App.Sessions;

namespace RDPManager.App.Tests;

/// <summary>A timer the test advances by hand.</summary>
internal sealed class ManualTimer : IDisposable
{
    private readonly Action? _tick;

    public ManualTimer(Action? tick = null) => _tick = tick;

    public bool Disposed { get; private set; }

    public void Tick(int times = 1)
    {
        for (var i = 0; i < times && !Disposed; i++)
        {
            _tick?.Invoke();
        }
    }

    public void Dispose() => Disposed = true;
}

/// <summary>A remote session the test drives by raising its events.</summary>
internal sealed class FakeRemoteSession : IRemoteSession
{
    public event EventHandler? Connected;

    public event EventHandler<DisconnectReport>? Disconnected;

    public event EventHandler? GoFullScreenRequested;

    public event EventHandler? LeaveFullScreenRequested;

    public event EventHandler? MinimizeRequested;

    public int ConnectCalls { get; private set; }

    public int DisconnectCalls { get; private set; }

    public bool? FullScreen { get; private set; }

    public void Connect() => ConnectCalls++;

    public void Disconnect() => DisconnectCalls++;

    public void SetFullScreen(bool fullScreen) => FullScreen = fullScreen;

    public void Focus()
    {
    }

    public void RaiseConnected() => Connected?.Invoke(this, EventArgs.Empty);

    public void RaiseDisconnected(int code, int extended = 0, string? description = null, bool userInitiated = false) =>
        Disconnected?.Invoke(this, new DisconnectReport(code, extended, description, userInitiated));

    public void RaiseLeaveFullScreen() => LeaveFullScreenRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseGoFullScreen() => GoFullScreenRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseMinimize() => MinimizeRequested?.Invoke(this, EventArgs.Empty);
}
