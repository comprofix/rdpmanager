using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace RDPManager.App.Services;

/// <summary>
/// Windows dialogs, owned by the main window when there is one: message boxes (user32 <c>MessageBoxW</c>)
/// and the Task Dialog for prompts with custom buttons.
/// </summary>
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

    public SaveChoice AskSaveChanges(string title, string message)
    {
        var save = new WinForms.TaskDialogButton("Save");
        var discard = new WinForms.TaskDialogButton("Discard");
        var cancel = WinForms.TaskDialogButton.Cancel;
        var page = new WinForms.TaskDialogPage
        {
            Caption = title,
            Heading = "Save changes?",
            Text = message,
            Icon = WinForms.TaskDialogIcon.Warning,
            Buttons = { save, discard, cancel },
            DefaultButton = save,
            AllowCancel = true,
        };

        var result = WinForms.TaskDialog.ShowDialog(getOwnerHandle(), page);
        return result == save ? SaveChoice.Save : result == discard ? SaveChoice.Discard : SaveChoice.Cancel;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
