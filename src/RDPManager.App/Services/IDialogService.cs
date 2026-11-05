namespace RDPManager.App.Services;

/// <summary>
/// Native dialogs (DESIGN: confirmations and errors use the operating system's dialogs, which also
/// appear above live RDP sessions). Windows uses its own message box; Linux gets its own implementation later.
/// </summary>
public interface IDialogService
{
    /// <summary>Asks a Yes/No question; No is the default. Returns true for Yes.</summary>
    bool Confirm(string title, string message);

    /// <summary>Shows an error with an OK button.</summary>
    void ShowError(string title, string message);
}
