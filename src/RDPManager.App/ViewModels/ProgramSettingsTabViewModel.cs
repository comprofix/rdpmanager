using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.App.Services;
using RDPManager.Core.Storage;

namespace RDPManager.App.ViewModels;

/// <summary>
/// The "Program settings" tab (File › Settings…): Appearance › Theme and Updates. Like every settings tab, changes
/// apply only when saved (DESIGN: Editing behaviour, owner decision 2026-11-05).
/// </summary>
public sealed partial class ProgramSettingsTabViewModel : TabViewModel
{
    public const string TabKey = "program-settings";
    private const string DialogTitle = "RDPManager";

    private readonly ProgramSettingsStore _store;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _applyTheme;
    private readonly bool _canSave;
    private ProgramSettings _saved;

    /// <param name="canSave">False when settings.json could not be read: saving would overwrite it, so it is refused.</param>
    public ProgramSettingsTabViewModel(ProgramSettingsStore store, ProgramSettings current, IDialogService dialogs, Action<string> applyTheme, bool canSave)
    {
        _store = store;
        _saved = current.Clone();
        _dialogs = dialogs;
        _applyTheme = applyTheme;
        _canSave = canSave;
        Title = "Program settings";
        _selectedTheme = Themes.First(t => t.Value == _saved.Theme);
        _checkForUpdatesAtStart = _saved.CheckForUpdatesAtStart;
        Refresh();
    }

    public override string Key => TabKey;

    public IReadOnlyList<ChoiceOption> Themes { get; } =
    [
        new(ProgramSettings.Themes.Light, "Light"),
        new(ProgramSettings.Themes.Dark, "Dark"),
        new(ProgramSettings.Themes.System, "Use system setting"),
    ];

    [ObservableProperty]
    private ChoiceOption _selectedTheme;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>The settings as last saved.</summary>
    public ProgramSettings Saved => _saved;

    /// <summary>Updates › "Check for updates when RDPManager starts" (off by default).</summary>
    [ObservableProperty]
    private bool _checkForUpdatesAtStart;

    /// <summary>The page shown: 0 Appearance, 1 Updates.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAppearance))]
    [NotifyPropertyChangedFor(nameof(ShowUpdates))]
    private int _selectedPage;

    public bool ShowAppearance => SelectedPage == 0;

    public bool ShowUpdates => SelectedPage == 1;

    partial void OnSelectedThemeChanged(ChoiceOption value) => Refresh();

    partial void OnCheckForUpdatesAtStartChanged(bool value) => Refresh();

    [RelayCommand(CanExecute = nameof(IsModified))]
    public async Task SaveAsync() => await TrySaveAsync();

    [RelayCommand(CanExecute = nameof(IsModified))]
    public async Task CancelAsync()
    {
        switch (_dialogs.AskSaveChanges(DialogTitle, "\"Program settings\" has unsaved changes."))
        {
            case SaveChoice.Save:
                await TrySaveAsync();
                break;
            case SaveChoice.Discard:
                SelectedTheme = Themes.First(t => t.Value == _saved.Theme);
                CheckForUpdatesAtStart = _saved.CheckForUpdatesAtStart;
                break;
        }
    }

    public override async Task<bool> PrepareToCloseAsync()
    {
        if (!IsModified)
        {
            return true;
        }

        return _dialogs.AskSaveChanges(DialogTitle, "\"Program settings\" has unsaved changes.") switch
        {
            SaveChoice.Save => await TrySaveAsync(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    private async Task<bool> TrySaveAsync()
    {
        if (!_canSave)
        {
            _dialogs.ShowError(DialogTitle, $"Program settings can't be saved because '{_store.FilePath}' could not be read when RDPManager started. Fix or remove that file, then restart.");
            return false;
        }

        var updated = _saved.Clone();
        updated.Theme = SelectedTheme.Value;
        updated.CheckForUpdatesAtStart = CheckForUpdatesAtStart;
        try
        {
            await _store.SaveAsync(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(DialogTitle, $"Program settings could not be saved.\n\n{ex.Message}");
            return false;
        }

        _saved = updated;
        _applyTheme(updated.Theme);
        Refresh();
        return true;
    }

    private void Refresh()
    {
        IsModified = SelectedTheme.Value != _saved.Theme || CheckForUpdatesAtStart != _saved.CheckForUpdatesAtStart;
        StatusText = IsModified ? "Unsaved changes" : "No unsaved changes";
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
