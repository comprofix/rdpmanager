using System.Text.Json;
using System.Text.Json.Nodes;
using RDPManager.Core.Model;

namespace RDPManager.Core.Settings;

/// <summary>
/// Unsaved edits to a connection or folder (DESIGN: nothing is persisted until Save). Values resolve
/// against the real tree, so inherited values and their sources show correctly. A new node is not part
/// of the tree until it is saved.
/// </summary>
public sealed class NodeDraft
{
    private readonly ConnectionTree _tree;
    private readonly SettingsResolver _resolver;
    private readonly TreeNode _shadow;
    private readonly Dictionary<string, JsonElement>? _unknownProperties;
    private JsonObject _savedSettings;
    private string _savedName;

    private NodeDraft(ConnectionTree tree, TreeNode shadow, bool isNew, Dictionary<string, JsonElement>? unknownProperties)
    {
        _tree = tree;
        _resolver = new SettingsResolver(tree);
        _shadow = shadow;
        _unknownProperties = unknownProperties;
        IsNew = isNew;
        _savedName = shadow.Name;
        _savedSettings = (JsonObject)shadow.Settings.DeepClone();
    }

    /// <summary>Starts editing an existing node.</summary>
    public static NodeDraft ForExisting(ConnectionTree tree, TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(node);
        var shadow = Shadow(node.Type, node.Id, node.Name, (JsonObject)node.Settings.DeepClone(), node.Parent);
        return new NodeDraft(tree, shadow, isNew: false, node.UnknownProperties);
    }

    /// <summary>Starts a new connection or folder inside <paramref name="parentId"/> (null: top level).</summary>
    public static NodeDraft ForNew(ConnectionTree tree, NodeType type, string? parentId)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var parent = parentId is null ? null : tree.Find(parentId) as FolderNode
            ?? throw new KeyNotFoundException($"No folder with id '{parentId}'.");
        return new NodeDraft(tree, Shadow(type, null, string.Empty, [], parent), isNew: true, null);
    }

    public string Id => _shadow.Id;

    public NodeType Type => _shadow.Type;

    public bool IsNew { get; private set; }

    /// <summary>The folder the node is (or will be) in; null for the top level.</summary>
    public FolderNode? Parent => _shadow.Parent;

    public string Name
    {
        get => _shadow.Name;
        set => _shadow.Name = value ?? string.Empty;
    }

    /// <summary>True when the draft differs from what was last saved (always true for a new node).</summary>
    public bool IsDirty => IsNew || Name != _savedName || !JsonNode.DeepEquals(_shadow.Settings, _savedSettings);

    /// <summary>The settings that apply to this node's type, in schema order.</summary>
    public IEnumerable<SettingDefinition> Definitions => SettingsSchema.All.Where(d => d.AppliesTo(Type));

    public ResolvedSetting Get(string key) => _resolver.Resolve(_shadow, SettingsSchema.Get(key));

    /// <summary>True when the value is set on this node rather than inherited.</summary>
    public bool IsSetHere(string key) => _shadow.Settings.ContainsKey(SettingsSchema.Get(key).StorageKey);

    /// <summary>Sets (overrides) a value on this node. Secrets cannot be set here.</summary>
    public void Set(string key, JsonNode? value)
    {
        var definition = SettingsSchema.Get(key);
        if (definition.Type == SettingType.Secret)
        {
            throw new InvalidOperationException("Passwords are stored in the operating system's credential store, not set here.");
        }

        if (!definition.AppliesTo(Type))
        {
            throw new InvalidOperationException($"'{key}' does not apply to a {Type}.");
        }

        _shadow.Settings[definition.StorageKey] = value?.DeepClone();
    }

    /// <summary>Removes this node's value, so it is inherited again.</summary>
    public void Reset(string key) => _shadow.Settings.Remove(SettingsSchema.Get(key).StorageKey);

    /// <summary>Problems that stop the draft being saved; empty when it can be saved.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Name: enter a name.");
        }

        if (Type == NodeType.Connection && string.IsNullOrWhiteSpace(Get("host").Value?.GetValue<string>()))
        {
            problems.Add("Host: enter the computer's name or address.");
        }

        foreach (var definition in Definitions.Where(d => d.Type != SettingType.Secret))
        {
            if (_shadow.Settings.TryGetPropertyValue(definition.StorageKey, out var value) && !definition.IsValid(value))
            {
                problems.Add(definition.Type == SettingType.Number && definition.Minimum is not null && definition.Maximum is not null
                    ? $"{definition.Label}: enter a number from {definition.Minimum} to {definition.Maximum}."
                    : $"{definition.Label}: the value is not valid.");
            }
        }

        return problems;
    }

    /// <summary>The node to save: same id for an existing node; values trimmed of surrounding spaces.</summary>
    public TreeNode ToNode()
    {
        var node = Type == NodeType.Folder ? (TreeNode)new FolderNode(Id) : new ConnectionNode(Id);
        node.Name = Name.Trim();
        node.Settings = (JsonObject)_shadow.Settings.DeepClone();
        foreach (var (key, value) in _shadow.Settings)
        {
            if (value is JsonValue v && v.TryGetValue<string>(out var text) && text != text.Trim())
            {
                node.Settings[key] = text.Trim();
            }
        }

        node.UnknownProperties = _unknownProperties;
        return node;
    }

    /// <summary>Records that the draft was saved: it is no longer new and has no unsaved changes.</summary>
    public void MarkSaved()
    {
        IsNew = false;
        _shadow.Name = _shadow.Name.Trim();
        _savedName = _shadow.Name;
        _savedSettings = (JsonObject)_shadow.Settings.DeepClone();
    }

    /// <summary>Throws away unsaved changes.</summary>
    public void Revert()
    {
        _shadow.Name = _savedName;
        _shadow.Settings = (JsonObject)_savedSettings.DeepClone();
    }

    /// <summary>The node was renamed in the tree; follow the new name unless the name was edited here.</summary>
    public void OnRenamedElsewhere(string newName)
    {
        if (Name == _savedName)
        {
            Name = newName;
        }

        _savedName = newName;
    }

    /// <summary>
    /// For a saved folder: how many connections inside it currently take <paramref name="key"/> from this folder
    /// (they don't set it themselves and no folder in between sets it).
    /// </summary>
    public int CountConnectionsUsing(string key)
    {
        if (Type != NodeType.Folder || IsNew || _tree.Find(Id) is not FolderNode folder)
        {
            return 0;
        }

        var definition = SettingsSchema.Get(key);
        if (!definition.Inheritable)
        {
            return 0;
        }

        return Count(folder);

        int Count(FolderNode current)
        {
            var total = 0;
            foreach (var child in current.Children)
            {
                if (child.Settings.ContainsKey(definition.StorageKey))
                {
                    continue;
                }

                total += child is FolderNode sub ? Count(sub) : 1;
            }

            return total;
        }
    }

    private static TreeNode Shadow(NodeType type, string? id, string name, JsonObject settings, FolderNode? parent)
    {
        TreeNode shadow = type == NodeType.Folder ? new FolderNode(id) : new ConnectionNode(id);
        shadow.Name = name;
        shadow.Settings = settings;
        shadow.Parent = parent;
        return shadow;
    }
}
