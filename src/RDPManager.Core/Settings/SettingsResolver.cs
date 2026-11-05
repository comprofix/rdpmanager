using System.Text.Json.Nodes;
using RDPManager.Core.Model;

namespace RDPManager.Core.Settings;

/// <summary>Where a resolved setting value came from.</summary>
public enum SettingSource
{
    /// <summary>Set on the node itself.</summary>
    Node,

    /// <summary>Inherited from a folder above the node (see <see cref="ResolvedSetting.SourceNode"/>).</summary>
    Ancestor,

    /// <summary>From the file-level defaults.</summary>
    FileDefault,

    /// <summary>The schema's built-in default.</summary>
    BuiltIn,
}

/// <summary>A setting's effective value and where it came from.</summary>
/// <param name="Definition">The setting.</param>
/// <param name="Value">A copy of the effective value (for secrets: true when a password is stored).</param>
/// <param name="Source">Where the value came from.</param>
/// <param name="SourceNode">The node or folder that set it, for <see cref="SettingSource.Node"/> and <see cref="SettingSource.Ancestor"/>.</param>
public sealed record ResolvedSetting(SettingDefinition Definition, JsonNode? Value, SettingSource Source, TreeNode? SourceNode)
{
    public bool IsInherited => Source != SettingSource.Node;
}

/// <summary>
/// Resolves effective settings: the node, then its folders (nearest first), then the file defaults,
/// then the built-in default. Settings that are not inheritable come from the node or the built-in default only.
/// </summary>
public sealed class SettingsResolver(ConnectionTree tree)
{
    private readonly ConnectionTree _tree = tree ?? throw new ArgumentNullException(nameof(tree));

    public ResolvedSetting Resolve(TreeNode node, string key) => Resolve(node, SettingsSchema.Get(key));

    public ResolvedSetting Resolve(TreeNode node, SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(definition);
        var storageKey = definition.StorageKey;

        if (node.Settings.TryGetPropertyValue(storageKey, out var own) && own is not null)
        {
            return new(definition, own.DeepClone(), SettingSource.Node, node);
        }

        if (definition.Inheritable)
        {
            foreach (var folder in node.Ancestors())
            {
                if (folder.Settings.TryGetPropertyValue(storageKey, out var inherited) && inherited is not null)
                {
                    return new(definition, inherited.DeepClone(), SettingSource.Ancestor, folder);
                }
            }

            if (_tree.Defaults.TryGetPropertyValue(storageKey, out var fileDefault) && fileDefault is not null)
            {
                return new(definition, fileDefault.DeepClone(), SettingSource.FileDefault, null);
            }
        }

        return new(definition, definition.Default?.DeepClone(), SettingSource.BuiltIn, null);
    }

    /// <summary>All settings that apply to the node's type, resolved.</summary>
    public IReadOnlyList<ResolvedSetting> ResolveAll(TreeNode node) =>
        SettingsSchema.All.Where(d => d.AppliesTo(node.Type)).Select(d => Resolve(node, d)).ToList();
}
