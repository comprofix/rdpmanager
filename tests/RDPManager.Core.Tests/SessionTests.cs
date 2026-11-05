using RDPManager.Core.Model;
using RDPManager.Core.Sessions;

namespace RDPManager.Core.Tests;

public class SessionTests
{
    [Fact]
    public void Options_UseResolvedSettingsAndDefaults()
    {
        var tree = new ConnectionTree();
        tree.Defaults["port"] = 3390;
        var folder = new FolderNode { Name = "Acme" };
        folder.Settings["username"] = "svc";
        folder.Settings["domain"] = "ACME";
        folder.Settings["gateway.usage"] = "always";
        folder.Settings["gateway.host"] = "gw.acme.test";
        tree.Add(folder);
        var dc = new ConnectionNode { Name = "acme-dc01" };
        dc.Settings["host"] = " dc.acme.test ";
        dc.Settings["display.resolution"] = "1280x720";
        dc.Settings["redirect.drives"] = true;
        dc.Settings["redirect.sound"] = "none";
        tree.Add(dc, folder.Id);

        var options = SessionOptions.For(tree, dc);

        Assert.Equal(dc.Id, options.ConnectionId);
        Assert.Equal("dc.acme.test", options.Host);
        Assert.Equal(3390, options.Port);
        Assert.Equal("svc", options.Username);
        Assert.Equal("ACME", options.Domain);
        Assert.Equal(DisplayMode.FixedSize, options.Display);
        Assert.Equal((1280, 720), (options.DesktopWidth, options.DesktopHeight));
        Assert.Equal(32, options.ColorDepth);
        Assert.True(options.NetworkLevelAuthentication);
        Assert.Equal(ServerAuthentication.Warn, options.ServerAuthentication);
        Assert.Equal(GatewayUsage.Always, options.GatewayUsage);
        Assert.Equal("gw.acme.test", options.GatewayHost);
        Assert.True(options.GatewaySameCredentials);
        Assert.True(options.RedirectClipboard);
        Assert.True(options.RedirectDrives);
        Assert.True(options.RedirectPrinters);
        Assert.Equal(SoundMode.DoNotPlay, options.Sound);
        Assert.True(options.AutoReconnect);
        Assert.Equal(PerformancePreset.Auto, options.Performance);
    }

    [Theory]
    [InlineData("fit", DisplayMode.FitToWindow)]
    [InlineData("fullScreen", DisplayMode.FullScreen)]
    [InlineData("1920x1080", DisplayMode.FixedSize)]
    public void Options_MapDisplayModes(string value, DisplayMode expected)
    {
        var tree = new ConnectionTree();
        var c = new ConnectionNode { Name = "c" };
        c.Settings["host"] = "h";
        c.Settings["display.resolution"] = value;
        tree.Add(c);

        Assert.Equal(expected, SessionOptions.For(tree, c).Display);
    }

    [Theory]
    [InlineData(1, 0, DisconnectKind.Disconnected)]   // Local disconnection.
    [InlineData(2, 12, DisconnectKind.Disconnected)]  // Remote by user, logged off.
    [InlineData(3, 5, DisconnectKind.Disconnected)]   // Replaced by another connection (spike).
    [InlineData(3, 3, DisconnectKind.Disconnected)]   // Idle timeout.
    [InlineData(3, 7, DisconnectKind.Error)]          // Server denied the connection.
    [InlineData(3, 258, DisconnectKind.Error)]        // No licence.
    [InlineData(260, 0, DisconnectKind.Error)]        // DNS lookup failed.
    [InlineData(2055, 0, DisconnectKind.Error)]       // Logon failed.
    [InlineData(0x3000008, 0, DisconnectKind.Error)]  // Gateway failure seen in the spike.
    [InlineData(0x300001D, 0, DisconnectKind.Error)]  // Gateway failure with no description (spike).
    public void Classify_UsesMicrosoftsNormalCodes(int code, int extended, DisconnectKind expected)
    {
        Assert.Equal(expected, DisconnectInfo.Classify(code, extended, null).Kind);
    }

    [Fact]
    public void Message_PrefersTheControlsDescription_ThenDocumentedMeaning_ThenAFallback()
    {
        Assert.Equal("There was a problem.", DisconnectInfo.Classify(0x3000008, 0, " There was a problem. ").Message);
        Assert.Equal(
            "The session was taken over by another connection to the remote computer.",
            DisconnectInfo.Classify(3, 5, "").Message);
        Assert.Equal("The computer's name could not be found (DNS lookup failed).", DisconnectInfo.Classify(260, 0, null).Message);

        var unknown = DisconnectInfo.Classify(0x300001D, 0, "");
        Assert.Equal("The connection failed.", unknown.Message);
        Assert.Equal("Code 0x300001D (50331677), extended 0", unknown.Details);
    }
}
