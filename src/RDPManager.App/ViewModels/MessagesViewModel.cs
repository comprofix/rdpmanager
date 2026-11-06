using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.Core.Logging;

namespace RDPManager.App.ViewModels;

/// <summary>A row in the Messages panel.</summary>
public sealed record MessageRow(LogEntry Entry)
{
    public string Time => Entry.Time.ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);

    public string Level => Entry.Level == LogLevel.Error ? "Error" : "Warning";

    public bool IsError => Entry.Level == LogLevel.Error;

    /// <summary>The first line; the rest is in the tooltip and the log file.</summary>
    public string Summary => Entry.Message.Split('\n', 2)[0].TrimEnd('\r');

    public string Message => Entry.Message;
}

/// <summary>
/// The Messages panel (DESIGN: Messages panel): this run's warnings and errors. It does not open by itself;
/// the status bar shows "N new errors", which opens it.
/// </summary>
public sealed partial class MessagesViewModel : ObservableObject
{
    private readonly Action<string> _openFolder;

    /// <param name="post">Runs an action on the UI thread (entries can be written from any thread).</param>
    /// <param name="openFolder">Opens a folder in the file manager.</param>
    public MessagesViewModel(IAppLog log, string logFolder, Action<Action> post, Action<string> openFolder)
    {
        ArgumentNullException.ThrowIfNull(log);
        LogFolder = logFolder;
        _openFolder = openFolder;
        log.EntryAdded += (_, entry) =>
        {
            if (entry.Level != LogLevel.Info)
            {
                post(() => Add(entry));
            }
        };
    }

    public string LogFolder { get; }

    public ObservableCollection<MessageRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MenuText))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewErrorsText))]
    [NotifyPropertyChangedFor(nameof(HasNewErrors))]
    private int _newErrors;

    public bool HasNewErrors => NewErrors > 0;

    /// <summary>Status bar link, for example "2 new errors".</summary>
    public string NewErrorsText => NewErrors == 1 ? "1 new error" : $"{NewErrors} new errors";

    public string MenuText => IsOpen ? "Hide _messages" : "_Messages";

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Clear()
    {
        Rows.Clear();
        NewErrors = 0;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
        _openFolder(LogFolder);
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            NewErrors = 0; // Seen.
        }
    }

    private void Add(LogEntry entry)
    {
        Rows.Insert(0, new MessageRow(entry)); // Newest first.
        if (entry.Level == LogLevel.Error && !IsOpen)
        {
            NewErrors++;
        }
    }
}
