using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDPManager.Core.Settings;

namespace RDPManager.App.ViewModels;

/// <summary>A choice in a drop-down: stored value and readable label.</summary>
public sealed record ChoiceOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One setting in a settings tab, generated from the schema. Shows the effective value; an inherited
/// value is greyed with its source ("Inherited from Acme", "Default"); changing it sets it here, and
/// Reset returns it to inherited (DESIGN: variant A).
/// </summary>
public sealed partial class SettingFieldViewModel : ObservableObject
{
    private readonly SettingsTabViewModel _owner;
    private bool _updating;
    private string _text = string.Empty;
    private bool _isChecked;
    private ChoiceOption? _selectedChoice;

    public SettingFieldViewModel(SettingDefinition definition, SettingsTabViewModel owner)
    {
        Definition = definition;
        _owner = owner;
        Choices = definition.Choices.Select(c => new ChoiceOption(c, definition.ChoiceLabel(c))).ToList();
    }

    public SettingDefinition Definition { get; }

    public string Key => Definition.Key;

    public string Label => Definition.Label;

    public string Group => Definition.Group;

    public bool IsTextLike => Definition.Type is SettingType.Text or SettingType.Number;

    public bool IsBoolean => Definition.Type == SettingType.Boolean;

    public bool IsChoice => Definition.Type == SettingType.Choice;

    public bool IsSecret => Definition.Type == SettingType.Secret;

    public IReadOnlyList<ChoiceOption> Choices { get; }

    /// <summary>Value for text and number fields.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty) && !_updating)
            {
                if (Definition.Type == SettingType.Number)
                {
                    _owner.Draft.Set(Key, int.TryParse(_text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                        ? JsonValue.Create(number)
                        : JsonValue.Create(_text)); // Invalid numbers are kept so Save can report them.
                }
                else
                {
                    _owner.Draft.Set(Key, JsonValue.Create(_text));
                }

                _owner.OnFieldChanged();
            }
        }
    }

    /// <summary>Value for true/false fields.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value) && !_updating)
            {
                _owner.Draft.Set(Key, JsonValue.Create(value));
                _owner.OnFieldChanged();
            }
        }
    }

    /// <summary>Value for choice fields.</summary>
    public ChoiceOption? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (SetProperty(ref _selectedChoice, value) && !_updating && value is not null)
            {
                _owner.Draft.Set(Key, JsonValue.Create(value.Value));
                _owner.OnFieldChanged();
            }
        }
    }

    private string _password = string.Empty;

    /// <summary>
    /// A newly typed password for password fields. Stored passwords are never read back into the field;
    /// it stays empty and shows <see cref="PasswordPlaceholder"/>. Clearing it drops the typed password.
    /// </summary>
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value ?? string.Empty) && !_updating)
            {
                if (_password.Length == 0)
                {
                    _owner.Draft.ClearPendingSecret(Key);
                }
                else
                {
                    _owner.Draft.SetSecret(Key, _password);
                }

                _owner.OnFieldChanged();
            }
        }
    }

    /// <summary>"●●●●●●●●" when a password is stored (here or inherited), otherwise a hint.</summary>
    [ObservableProperty]
    private string _passwordPlaceholder = string.Empty;

    /// <summary>True when the value is not set on this item (shown greyed).</summary>
    [ObservableProperty]
    private bool _isInherited;

    /// <summary>"Inherited from Acme", "Default", "Set here", "Set here · used by 2 connections", or empty.</summary>
    [ObservableProperty]
    private string _sourceText = string.Empty;

    /// <summary>True when <see cref="SourceText"/> names a folder that can be shown in the tree.</summary>
    [ObservableProperty]
    private bool _hasSourceLink;

    [ObservableProperty]
    private bool _canReset;

    private string? _sourceNodeId;

    [RelayCommand]
    private void Reset()
    {
        _owner.Draft.Reset(Key);
        _owner.OnFieldChanged();
    }

    [RelayCommand]
    private void GoToSource()
    {
        if (_sourceNodeId is not null)
        {
            _owner.ShowInTree(_sourceNodeId);
        }
    }

    /// <summary>The gateway credential fields, which are not used while the gateway shares the connection's credentials.</summary>
    private static readonly HashSet<string> GatewayCredentialKeys = ["gateway.username", "gateway.domain", "gateway.password"];

    /// <summary>False when the field can't be edited (gateway credentials while "same credentials" is ticked).</summary>
    [ObservableProperty]
    private bool _isEditable = true;

    /// <summary>Reloads the shown value and source from the draft.</summary>
    public void Refresh()
    {
        RefreshValue();

        // Values entered earlier are kept; they are used again if "same credentials" is unticked.
        IsEditable = !(GatewayCredentialKeys.Contains(Key)
            && _owner.Draft.Get("gateway.sameCredentials").Value is JsonValue same
            && same.TryGetValue<bool>(out var shared) && shared);
        if (!IsEditable)
        {
            SourceText = "Uses the connection's credentials";
            HasSourceLink = false;
            CanReset = false;
        }
    }

    private void RefreshValue()
    {
        _updating = true;
        try
        {
            var resolved = _owner.Draft.Get(Key);
            var setHere = _owner.Draft.IsSetHere(Key);

            if (IsSecret)
            {
                RefreshSecret(resolved, setHere);
                return;
            }
            var value = resolved.Value as JsonValue;

            switch (Definition.Type)
            {
                case SettingType.Boolean:
                    IsChecked = value is not null && value.TryGetValue<bool>(out var b) && b;
                    break;
                case SettingType.Choice:
                    var stored = value is not null && value.TryGetValue<string>(out var s) ? s : null;
                    SelectedChoice = Choices.FirstOrDefault(c => c.Value == stored);
                    break;
                default:
                    Text = value is null ? string.Empty
                        : value.TryGetValue<int>(out var n) ? n.ToString(CultureInfo.InvariantCulture)
                        : value.TryGetValue<string>(out var t) ? t
                        : value.ToJsonString();
                    break;
            }

            IsInherited = !setHere && Definition.Inheritable;
            CanReset = setHere && (Definition.Inheritable || !string.IsNullOrEmpty(Text));
            _sourceNodeId = resolved.Source == SettingSource.Ancestor ? resolved.SourceNode?.Id : null;
            HasSourceLink = !setHere && _sourceNodeId is not null;
            SourceText = DescribeSource(resolved, setHere);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Password fields (DESIGN: Credentials, Windows details).</summary>
    private void RefreshSecret(ResolvedSetting resolved, bool setHere)
    {
        var pending = _owner.Draft.PendingSecrets.TryGetValue(Key, out var p) ? p : null;
        Password = pending ?? string.Empty;
        var stored = resolved.Value is JsonValue v && v.TryGetValue<bool>(out var b) && b;

        IsInherited = !setHere;
        CanReset = setHere;
        _sourceNodeId = !setHere && stored && resolved.Source == SettingSource.Ancestor ? resolved.SourceNode?.Id : null;
        HasSourceLink = _sourceNodeId is not null;
        PasswordPlaceholder = stored && pending is null ? "●●●●●●●●" : "Not stored: Windows will ask";

        if (pending is not null)
        {
            SourceText = "Stored in Credential Manager on Save";
        }
        else if (setHere)
        {
            var users = _owner.Draft.CountConnectionsUsing(Key);
            SourceText = users switch
            {
                0 => "Stored in Credential Manager",
                1 => "Stored in Credential Manager · used by 1 connection",
                _ => $"Stored in Credential Manager · used by {users} connections",
            };
        }
        else if (HasSourceLink)
        {
            SourceText = $"Inherited from {resolved.SourceNode?.Name}";
        }
        else
        {
            SourceText = _owner.Draft.PendingSecrets.ContainsKey(Key) ? "Removed on Save" : "Not stored";
        }
    }

    private string DescribeSource(ResolvedSetting resolved, bool setHere)
    {
        if (!Definition.Inheritable)
        {
            return string.Empty;
        }

        if (setHere)
        {
            var users = _owner.Draft.CountConnectionsUsing(Key);
            return users switch
            {
                0 => "Set here",
                1 => "Set here · used by 1 connection",
                _ => $"Set here · used by {users} connections",
            };
        }

        return resolved.Source switch
        {
            SettingSource.Ancestor => $"Inherited from {resolved.SourceNode?.Name}",
            SettingSource.FileDefault => "File default",
            _ => "Default",
        };
    }
}
