using CommunityToolkit.Mvvm.ComponentModel;

namespace RDPManager.App.ViewModels;

/// <summary>A tab in the right-hand tab strip (settings tabs now; session tabs in milestone 7).</summary>
public abstract partial class TabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>True when the tab has unsaved changes (shown as a dot on the tab).</summary>
    [ObservableProperty]
    private bool _isModified;

    /// <summary>True for a session tab that is not connected (its title is shown in italics).</summary>
    [ObservableProperty]
    private bool _isInactive;

    /// <summary>Identifies what the tab shows, so opening the same thing again switches to the existing tab.</summary>
    public abstract string Key { get; }

    /// <summary>
    /// Called before the tab closes. Returns false to keep it open (for example the user chose Cancel at
    /// the unsaved-changes prompt).
    /// </summary>
    public abstract Task<bool> PrepareToCloseAsync();
}
