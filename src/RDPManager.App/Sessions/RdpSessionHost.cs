using RDPManager.App.ViewModels;
using RoyalApps.Community.Avalonia.Windows.NativeControls;
using RoyalApps.Community.Rdp.WinForms;
using WinForms = System.Windows.Forms;

namespace RDPManager.App.Sessions;

/// <summary>
/// Hosts a session's RDP control in Avalonia. The control is created once per session tab (its data context)
/// and survives tab switches, undocking and full screen; the tab releases it when it closes.
/// </summary>
public sealed class RdpSessionHost : WinFormsControlHost<RdpControl>
{
    protected override RdpControl OnCreateWinFormsControl()
    {
        if (DataContext is not SessionTabViewModel tab)
        {
            throw new InvalidOperationException("RdpSessionHost needs a SessionTabViewModel data context.");
        }

        var control = new RdpControl { Dock = WinForms.DockStyle.Fill };
        RdpConfigurationMapper.Apply(tab.Options, tab.Secrets, control.RdpConfiguration);
        var session = new RdpRemoteSession(control);

        // Connect once the native window exists; later re-attachments reuse the live control.
        var attached = false;
        control.HandleCreated += (_, _) =>
        {
            if (!attached)
            {
                attached = true;
                control.BeginInvoke(() => tab.Attach(session));
            }
        };

        return control;
    }
}
