using System.Text.Json.Nodes;
using RDPManager.Core.Model;

namespace RDPManager.Core.Settings;

/// <summary>The kind of value a setting holds.</summary>
public enum SettingType
{
    Text,
    Number,
    Boolean,
    Choice,

    /// <summary>A secret (password). Only a "stored" marker is kept in the file; the secret lives in the OS credential store.</summary>
    Secret,
}

/// <summary>Which node types a setting can be set on.</summary>
[Flags]
public enum SettingScope
{
    Folder = 1,
    Connection = 2,
    Both = Folder | Connection,
}

/// <summary>
/// One setting in the schema. The schema drives validation, built-in defaults, inheritance and
/// (later) the generated settings UI.
/// </summary>
public sealed record SettingDefinition
{
    public required string Key { get; init; }

    /// <summary>Settings-tab group, for example "Basics" or "RD Gateway".</summary>
    public required string Group { get; init; }

    public required SettingType Type { get; init; }

    /// <summary>Built-in default, used when nothing in the tree or file defaults sets a value.</summary>
    public JsonNode? Default { get; init; }

    /// <summary>True when folders and file defaults can supply the value.</summary>
    public bool Inheritable { get; init; } = true;

    public SettingScope Scope { get; init; } = SettingScope.Both;

    /// <summary>Allowed values for <see cref="SettingType.Choice"/>.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>The key used in the file: secrets are stored as a <c>&lt;key&gt;Stored</c> marker only.</summary>
    public string StorageKey => Type == SettingType.Secret ? Key + "Stored" : Key;

    public bool AppliesTo(NodeType type) =>
        type == NodeType.Folder ? Scope.HasFlag(SettingScope.Folder) : Scope.HasFlag(SettingScope.Connection);

    /// <summary>True when <paramref name="value"/> is valid for this setting.</summary>
    public bool IsValid(JsonNode? value)
    {
        if (value is not JsonValue v)
        {
            return false;
        }

        return Type switch
        {
            SettingType.Text => v.TryGetValue<string>(out _),
            SettingType.Number => v.TryGetValue<int>(out _),
            SettingType.Boolean or SettingType.Secret => v.TryGetValue<bool>(out _),
            SettingType.Choice => v.TryGetValue<string>(out var s) && Choices.Contains(s),
            _ => false,
        };
    }
}
