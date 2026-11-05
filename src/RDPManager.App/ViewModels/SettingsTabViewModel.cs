using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.App.Services;
using RDPManager.Core.Model;
using RDPManager.Core.Settings;

namespace RDPManager.App.ViewModels;

/// <summary>
/// A connection or folder settings tab (DESIGN: Agreed layout, Editing behaviour). Edits a draft;
/// nothing is saved until Save. Cancel or closing with unsaved changes asks Save, Discard or Cancel.
/// </summary>
public sealed partial class SettingsTabViewModel : TabViewModel
{
    private const string DialogTitle = "RDPManager";

    private readonly ConnectionTreeViewModel _tree;
    private readonly IDialogService _dialogs;
    private readonly Action<SettingsTabViewModel> _close;

    public SettingsTabViewModel(NodeDraft draft, ConnectionTreeViewModel tree, IDialogService dialogs, Action<SettingsTabViewModel> close)
    {
        Draft = draft;
        _tree = tree;
        _dialogs = dialogs;
        _close = close;
        Fields = draft.Definitions.Select(d => new SettingFieldViewModel(d, this)).ToList();
        Groups = SettingsSchema.Groups.All.Where(g => Fields.Any(f => f.Group == g)).ToList();
        _selectedGroup = Groups[0];
        SavedName = draft.Name;
        Refresh();
    }

    public NodeDraft Draft { get; }

    public override string Key => KeyFor(Draft.Id);

    public static string KeyFor(string nodeId) => "settings:" + nodeId;

    public bool IsFolder => Draft.Type == NodeType.Folder;

    public IReadOnlyList<string> Groups { get; }

    public IReadOnlyList<SettingFieldViewModel> Fields { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleFields))]
    [NotifyPropertyChangedFor(nameof(ShowNameField))]
    private string _selectedGroup;

    /// <summary>The fields of the selected group.</summary>
    public IEnumerable<SettingFieldViewModel> VisibleFields => Fields.Where(f => f.Group == SelectedGroup);

    /// <summary>The name is edited at the top of Basics.</summary>
    public bool ShowNameField => SelectedGroup == SettingsSchema.Groups.Basics;

    public string Name
    {
        get => Draft.Name;
        set
        {
            if (Draft.Name != value)
            {
                Draft.Name = value;
                OnPropertyChanged();
                OnFieldChanged();
            }
        }
    }

    /// <summary>For example "Personal › Clients › Acme".</summary>
    public string Breadcrumb =>
        string.Join(" › ", new[] { _tree.SourceName }.Concat(AncestorNames()));

    /// <summary>The heading above the fields.</summary>
    public string Heading => Draft.IsNew ? (IsFolder ? "New folder" : "New connection") : SavedName;

    [ObservableProperty]
    private string _statusText = string.Empty;

    private string SavedName { get; set; } = string.Empty;

    /// <summary>Called by a field after it changed the draft.</summary>
    internal void OnFieldChanged() => Refresh();

    /// <summary>Selects a folder in the tree (the "Inherited from …" link).</summary>
    internal void ShowInTree(string nodeId) => _tree.SelectNode(nodeId);

    [RelayCommand(CanExecute = nameof(IsModified))]
    public async Task SaveAsync() => await TrySaveAsync();

    [RelayCommand(CanExecute = nameof(IsModified))]
    public async Task CancelAsync()
    {
        switch (_dialogs.AskSaveChanges(DialogTitle, $"\"{Title}\" has unsaved changes."))
        {
            case SaveChoice.Save:
                await TrySaveAsync();
                break;
            case SaveChoice.Discard:
                Discard();
                break;
        }
    }

    public override async Task<bool> PrepareToCloseAsync()
    {
        if (!IsModified)
        {
            return true;
        }

        return _dialogs.AskSaveChanges(DialogTitle, $"\"{Title}\" has unsaved changes.") switch
        {
            SaveChoice.Save => await TrySaveAsync(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>The item was renamed in the tree.</summary>
    public void OnRenamedElsewhere(string newName)
    {
        Draft.OnRenamedElsewhere(newName);
        SavedName = newName;
        OnPropertyChanged(nameof(Name));
        Refresh();
    }

    private async Task<bool> TrySaveAsync()
    {
        var problems = Draft.Validate();
        if (problems.Count > 0)
        {
            _dialogs.ShowError(DialogTitle, "The changes can't be saved yet:\n\n" + string.Join("\n", problems));
            return false;
        }

        if (!await _tree.SaveDraftAsync(Draft))
        {
            return false;
        }

        Draft.MarkSaved();
        SavedName = Draft.Name;
        OnPropertyChanged(nameof(Name));
        Refresh();
        return true;
    }

    private void Discard()
    {
        if (Draft.IsNew)
        {
            _close(this); // Nothing was created.
            return;
        }

        Draft.Revert();
        OnPropertyChanged(nameof(Name));
        Refresh();
    }

    private IEnumerable<string> AncestorNames()
    {
        var names = new List<string>();
        for (var folder = Draft.Parent; folder is not null; folder = folder.Parent)
        {
            names.Insert(0, folder.Name);
        }

        return names;
    }

    private void Refresh()
    {
        IsModified = Draft.IsDirty;
        Title = $"{(Draft.IsNew ? (IsFolder ? "New folder" : "New connection") : SavedName)} - Settings";
        StatusText = IsModified ? "Unsaved changes" : "No unsaved changes";
        foreach (var field in Fields)
        {
            field.Refresh();
        }

        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Breadcrumb));
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
