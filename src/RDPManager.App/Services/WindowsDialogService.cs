using System.Runtime.InteropServices;

namespace RDPManager.App.Services;

/// <summary>Windows message boxes (user32 <c>MessageBoxW</c>), owned by the main window when there is one.</summary>
public sealed class WindowsDialogService(Func<nint> getOwnerHandle) : IDialogService
{
    private const uint MbOk = 0x0;
    private const uint MbYesNo = 0x4;
    private const uint MbIconError = 0x10;
    private const uint MbIconWarning = 0x30;
    private const uint MbDefButton2 = 0x100;
    private const int IdYes = 6;

    public bool Confirm(string title, string message) =>
        MessageBoxW(getOwnerHandle(), message, title, MbYesNo | MbIconWarning | MbDefButton2) == IdYes;

    public void ShowError(string title, string message) =>
        MessageBoxW(getOwnerHandle(), message, title, MbOk | MbIconError);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
