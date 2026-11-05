namespace RDPManager.App.Services;

/// <summary>The answer to "Save changes?".</summary>
public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// Native dialogs (DESIGN: confirmations and errors use the operating system's dialogs, which also
/// appear above live RDP sessions). Windows uses its own dialogs; Linux gets its own implementation later.
/// </summary>
public interface IDialogService
{
    /// <summary>Asks a Yes/No question; No is the default. Returns true for Yes.</summary>
    bool Confirm(string title, string message);

    /// <summary>Shows an error with an OK button.</summary>
    void ShowError(string title, string message);

    /// <summary>Asks whether to save unsaved changes: Save (default), Discard or Cancel.</summary>
    SaveChoice AskSaveChanges(string title, string message);
}
