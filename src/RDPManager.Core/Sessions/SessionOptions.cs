using RDPManager.Core.Model;
using RDPManager.Core.Settings;

namespace RDPManager.Core.Sessions;

public enum DisplayMode
{
    FitToWindow,
    FullScreen,
    FixedSize,
}

public enum ServerAuthentication
{
    /// <summary>Connect and don't warn.</summary>
    None,

    /// <summary>Warn me (mstsc's default).</summary>
    Warn,

    /// <summary>Do not connect.</summary>
    Required,
}

public enum GatewayUsage
{
    Never,
    Always,
    BypassLocal,
}

public enum SoundMode
{
    PlayLocally,
    PlayRemotely,
    DoNotPlay,
}

public enum PerformancePreset
{
    Auto,
    Lan,
    Broadband,
    Modem,
}

/// <summary>
/// Everything a session needs to connect, resolved from the connection's settings (node, its folders,
/// file defaults, built-in defaults). Platform-neutral: the Windows RDP control and, later, FreeRDP map from it.
/// Passwords are not included; they come from the credential store (<see cref="Credentials.SessionSecrets"/>)
/// or the control's own prompt.
/// </summary>
public sealed record SessionOptions
{
    public required string ConnectionId { get; init; }

    public required string Name { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; } = 3389;

    public string Username { get; init; } = string.Empty;

    public string Domain { get; init; } = string.Empty;

    public DisplayMode Display { get; init; } = DisplayMode.FitToWindow;

    /// <summary>Width and height for <see cref="DisplayMode.FixedSize"/>.</summary>
    public int DesktopWidth { get; init; }

    public int DesktopHeight { get; init; }

    public int ColorDepth { get; init; } = 32;

    public bool NetworkLevelAuthentication { get; init; } = true;

    public ServerAuthentication ServerAuthentication { get; init; } = ServerAuthentication.Warn;

    public GatewayUsage GatewayUsage { get; init; } = GatewayUsage.Never;

    public string GatewayHost { get; init; } = string.Empty;

    public bool GatewaySameCredentials { get; init; } = true;

    public string GatewayUsername { get; init; } = string.Empty;

    public string GatewayDomain { get; init; } = string.Empty;

    public bool RedirectClipboard { get; init; } = true;

    public bool RedirectDrives { get; init; }

    public bool RedirectPrinters { get; init; } = true;

    public SoundMode Sound { get; init; } = SoundMode.PlayLocally;

    public bool AdminSession { get; init; }

    public bool AutoReconnect { get; init; } = true;

    public PerformancePreset Performance { get; init; } = PerformancePreset.Auto;

    /// <summary>Resolves the options for a connection in its tree.</summary>
    public static SessionOptions For(ConnectionTree tree, ConnectionNode connection)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(connection);
        var resolver = new SettingsResolver(tree);

        string Text(string key) => resolver.Resolve(connection, key).Value?.GetValue<string>()?.Trim() ?? string.Empty;
        bool Flag(string key) => resolver.Resolve(connection, key).Value?.GetValue<bool>() ?? false;
        int Number(string key) => resolver.Resolve(connection, key).Value?.GetValue<int>() ?? 0;

        var resolution = Text("display.resolution");
        var (display, width, height) = resolution switch
        {
            "fullScreen" => (DisplayMode.FullScreen, 0, 0),
            "fit" or "" => (DisplayMode.FitToWindow, 0, 0),
            _ => ParseSize(resolution),
        };

        return new SessionOptions
        {
            ConnectionId = connection.Id,
            Name = connection.Name,
            Host = Text("host"),
            Port = Number("port") is > 0 and <= 65535 and var port ? port : 3389,
            Username = Text("username"),
            Domain = Text("domain"),
            Display = display,
            DesktopWidth = width,
            DesktopHeight = height,
            ColorDepth = int.TryParse(Text("display.colorDepth"), out var depth) ? depth : 32,
            NetworkLevelAuthentication = Flag("security.nla"),
            ServerAuthentication = Text("security.serverAuthentication") switch
            {
                "none" => ServerAuthentication.None,
                "required" => ServerAuthentication.Required,
                _ => ServerAuthentication.Warn,
            },
            GatewayUsage = Text("gateway.usage") switch
            {
                "always" => GatewayUsage.Always,
                "bypassLocal" => GatewayUsage.BypassLocal,
                _ => GatewayUsage.Never,
            },
            GatewayHost = Text("gateway.host"),
            GatewaySameCredentials = Flag("gateway.sameCredentials"),
            GatewayUsername = Text("gateway.username"),
            GatewayDomain = Text("gateway.domain"),
            RedirectClipboard = Flag("redirect.clipboard"),
            RedirectDrives = Flag("redirect.drives"),
            RedirectPrinters = Flag("redirect.printers"),
            Sound = Text("redirect.sound") switch
            {
                "remote" => SoundMode.PlayRemotely,
                "none" => SoundMode.DoNotPlay,
                _ => SoundMode.PlayLocally,
            },
            AdminSession = Flag("session.admin"),
            AutoReconnect = Flag("session.autoReconnect"),
            Performance = Text("performance.preset") switch
            {
                "lan" => PerformancePreset.Lan,
                "broadband" => PerformancePreset.Broadband,
                "modem" => PerformancePreset.Modem,
                _ => PerformancePreset.Auto,
            },
        };
    }

    private static (DisplayMode Mode, int Width, int Height) ParseSize(string value)
    {
        var parts = value.Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0
            ? (DisplayMode.FixedSize, w, h)
            : (DisplayMode.FitToWindow, 0, 0);
    }
}
