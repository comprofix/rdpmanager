using RDPManager.Core.Logging;

namespace RDPManager.App.Services;

/// <summary>Writes every error shown in a dialog to the log (DESIGN: Log file), then shows it.</summary>
public sealed class LoggingDialogService(IDialogService inner, IAppLog log) : IDialogService
{
    public bool Confirm(string title, string message) => inner.Confirm(title, message);

    public void ShowError(string title, string message)
    {
        log.Write(LogLevel.Error, message);
        inner.ShowError(title, message);
    }

    public SaveChoice AskSaveChanges(string title, string message) => inner.AskSaveChanges(title, message);

    public bool ConfirmExit(string title, string message) => inner.ConfirmExit(title, message);

    public void ShowInfo(string title, string message) => inner.ShowInfo(title, message);
}
