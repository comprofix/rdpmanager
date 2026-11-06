using RDPManager.Core.Credentials;
using RDPManager.Core.Sessions;
using RoyalApps.Community.Rdp.WinForms.Configuration;
using RoyalApps.Community.Rdp.WinForms.Configuration.Connection;
using RoyalApps.Community.Rdp.WinForms.Configuration.Display;
using RoyalApps.Community.Rdp.WinForms.Configuration.Performance;
using CoreGateway = RDPManager.Core.Sessions.GatewayUsage;
using WrapperGateway = RoyalApps.Community.Rdp.WinForms.Configuration.Connection.GatewayUsageMethod;

namespace RDPManager.App.Sessions;

/// <summary>
/// Maps the resolved first-version settings onto the Royal Apps RDP wrapper's configuration
/// (DESIGN: RDP engine; RDP spike findings).
/// </summary>
internal static class RdpConfigurationMapper
{
    public static void Apply(SessionOptions o, SessionSecrets secrets, RdpClientConfiguration config)
    {
        config.SessionMode = RdpSessionMode.Embedded;
        config.Server = o.Host;
        config.Port = o.Port;

        // Without a stored password the control's own Windows credential prompt asks (DESIGN: Credentials).
        config.Credentials.Username = o.Username;
        config.Credentials.Domain = o.Domain;
        if (!string.IsNullOrEmpty(secrets.Password))
        {
            config.Credentials.Password = new SensitiveString(secrets.Password);
        }
        config.Credentials.NetworkLevelAuthentication = o.NetworkLevelAuthentication;

        config.Display.AutoScaling = true;
        config.Display.DisplayConnectionBar = false; // The app's own full-screen drop-down bar replaces it.
        switch (o.Display)
        {
            case DisplayMode.FixedSize:
                config.Display.DesktopWidth = o.DesktopWidth;
                config.Display.DesktopHeight = o.DesktopHeight;
                config.Display.ResizeBehavior = ResizeBehavior.Scrollbars;
                break;
            default:
                config.Display.ResizeBehavior = ResizeBehavior.UpdateDesktopSize;
                break;
        }

        config.Display.ColorDepth = o.ColorDepth switch
        {
            15 => ColorDepth.ColorDepth15Bpp,
            16 => ColorDepth.ColorDepth16Bpp,
            24 => ColorDepth.ColorDepth24Bpp,
            _ => ColorDepth.ColorDepth32Bpp,
        };

        config.Security.AuthenticationLevel = o.ServerAuthentication switch
        {
            ServerAuthentication.None => AuthenticationLevel.NoAuthenticationOfServer,
            ServerAuthentication.Required => AuthenticationLevel.ServerAuthenticationRequired,
            _ => AuthenticationLevel.PromptIfAuthenticationFails,
        };
        config.Security.ConnectToAdministerServer = o.AdminSession;

        if (o.GatewayUsage == CoreGateway.Never || string.IsNullOrWhiteSpace(o.GatewayHost))
        {
            config.Gateway.GatewayUsageMethod = WrapperGateway.Never;
        }
        else
        {
            config.Gateway.GatewayUsageMethod = o.GatewayUsage == CoreGateway.BypassLocal
                ? WrapperGateway.BypassLocalAddresses
                : WrapperGateway.Always;
            config.Gateway.GatewayProfileUsageMethod = GatewayProfileUsageMethod.Explicit;
            config.Gateway.GatewayHostname = o.GatewayHost;
            config.Gateway.GatewayCredsSource = GatewayCredentialSource.UsernameAndPassword;
            config.Gateway.GatewayCredSharing = o.GatewaySameCredentials;
            config.Gateway.GatewayUsername = o.GatewaySameCredentials ? o.Username : o.GatewayUsername;
            config.Gateway.GatewayDomain = o.GatewaySameCredentials ? o.Domain : o.GatewayDomain;
            if (!string.IsNullOrEmpty(secrets.GatewayPassword))
            {
                config.Gateway.GatewayPassword = new SensitiveString(secrets.GatewayPassword);
            }
        }

        config.Redirection.RedirectClipboard = o.RedirectClipboard;
        config.Redirection.RedirectDrives = o.RedirectDrives;
        config.Redirection.RedirectPrinters = o.RedirectPrinters;
        config.Redirection.AudioRedirectionMode = o.Sound switch
        {
            SoundMode.PlayRemotely => AudioRedirectionMode.PlayRemote,
            SoundMode.DoNotPlay => AudioRedirectionMode.DisableSound,
            _ => AudioRedirectionMode.RedirectToClient,
        };

        config.Connection.EnableAutoReconnect = o.AutoReconnect;

        config.Performance.NetworkConnectionType = o.Performance switch
        {
            PerformancePreset.Lan => NetworkConnectionType.LAN,
            PerformancePreset.Broadband => NetworkConnectionType.BroadbandHigh,
            PerformancePreset.Modem => NetworkConnectionType.Modem,
            _ => NetworkConnectionType.Automatic,
        };

        config.Input.EnableWindowsKey = true;
    }
}
