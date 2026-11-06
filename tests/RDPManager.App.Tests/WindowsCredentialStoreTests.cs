using RDPManager.App.Services;

namespace RDPManager.App.Tests;

/// <summary>
/// Uses the real Windows Credential Manager with a test-only entry ("RDPManager.Tests:&lt;random id&gt;")
/// that the test removes again (owner approved, 2026-11-06).
/// </summary>
public sealed class WindowsCredentialStoreTests : IDisposable
{
    private readonly WindowsCredentialStore _store = new();
    private readonly string _target = "RDPManager.Tests:" + Guid.NewGuid().ToString("N");

    public void Dispose() => _store.Delete(_target);

    [Fact]
    public void WriteReadReplaceDelete_RoundTrips()
    {
        Assert.Null(_store.Read(_target));

        _store.Write(_target, "first pässword ✓", "test entry");
        Assert.Equal("first pässword ✓", _store.Read(_target));

        _store.Write(_target, "second", "test entry");
        Assert.Equal("second", _store.Read(_target));

        Assert.True(_store.Delete(_target));
        Assert.Null(_store.Read(_target));
        Assert.False(_store.Delete(_target));
    }
}
