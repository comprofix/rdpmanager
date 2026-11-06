using RDPManager.Core.Model;
using RDPManager.Core.Settings;

namespace RDPManager.Core.Credentials;

/// <summary>Passwords for a session. <see cref="ToString"/> never shows them.</summary>
public sealed record SessionSecrets(string? Password, string? GatewayPassword)
{
    public static SessionSecrets None { get; } = new(null, null);

    public override string ToString() => "SessionSecrets { (hidden) }";
}

/// <summary>
/// Keeps passwords in the credential store and the "password stored" markers in the connection file in step
/// (DESIGN: Credentials, Windows details). Entries are named <c>RDPManager:&lt;node id&gt;:&lt;setting key&gt;</c>.
/// </summary>
public sealed class CredentialService(ICredentialStore store)
{
    public const string TargetPrefix = "RDPManager:";

    public ICredentialStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The password settings (server and gateway).</summary>
    public static IEnumerable<SettingDefinition> SecretDefinitions =>
        SettingsSchema.All.Where(d => d.Type == SettingType.Secret);

    public static string Target(string nodeId, string key) => $"{TargetPrefix}{nodeId}:{key}";

    /// <summary>
    /// The password that applies to <paramref name="node"/> for <paramref name="key"/>: its own, or the nearest
    /// folder's. Null when none is stored, or when the marker points at an entry that no longer exists.
    /// </summary>
    public string? Resolve(ConnectionTree tree, TreeNode node, string key)
    {
        var resolved = new SettingsResolver(tree).Resolve(node, key);
        var stored = resolved.Value is not null && resolved.Value.GetValueKind() == System.Text.Json.JsonValueKind.True;
        return stored && resolved.SourceNode is { } source ? Store.Read(Target(source.Id, key)) : null;
    }

    /// <summary>The passwords a session connects with. The gateway uses the server password when it shares credentials.</summary>
    public SessionSecrets ForSession(ConnectionTree tree, ConnectionNode connection, bool gatewaySameCredentials)
    {
        var password = Resolve(tree, connection, "password");
        var gateway = gatewaySameCredentials ? password : Resolve(tree, connection, "gateway.password");
        return new SessionSecrets(password, gateway);
    }

    /// <summary>
    /// Writes the new passwords of a draft (null values are removals, done on <see cref="SecretChange.Commit"/>).
    /// If a write fails, the writes already made are undone and the exception is rethrown.
    /// </summary>
    public SecretChange Begin(string nodeId, string nodeName, IReadOnlyDictionary<string, string?> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var change = new SecretChange(Store);
        try
        {
            foreach (var (key, secret) in pending)
            {
                var target = Target(nodeId, key);
                if (secret is null)
                {
                    change.DeleteOnCommit(target);
                }
                else
                {
                    change.Write(target, secret, nodeName);
                }
            }
        }
        catch (CredentialStoreException)
        {
            change.Rollback();
            throw;
        }

        return change;
    }

    /// <summary>
    /// Copies the passwords stored on a duplicated subtree to the copy and sets the copy's markers.
    /// <paramref name="idMap"/> maps original ids to copy ids. Entries that no longer exist are skipped.
    /// </summary>
    public SecretChange CopyTo(ConnectionTree tree, TreeNode copyRoot, IReadOnlyDictionary<string, string> idMap)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(copyRoot);
        ArgumentNullException.ThrowIfNull(idMap);
        var copies = SelfAndDescendants(copyRoot).ToDictionary(n => n.Id);
        var change = new SecretChange(Store);
        try
        {
            foreach (var (originalId, copyId) in idMap)
            {
                if (tree.Find(originalId) is not { } original || !copies.TryGetValue(copyId, out var copy))
                {
                    continue;
                }

                foreach (var definition in SecretDefinitions)
                {
                    if (!original.Settings.ContainsKey(definition.StorageKey)
                        || Store.Read(Target(originalId, definition.Key)) is not { } secret)
                    {
                        continue;
                    }

                    change.Write(Target(copyId, definition.Key), secret, copy.Name);
                    copy.Settings[definition.StorageKey] = true;
                }
            }
        }
        catch (CredentialStoreException)
        {
            change.Rollback();
            throw;
        }

        return change;
    }

    /// <summary>Removes the passwords of deleted nodes. Returns the targets that could not be removed.</summary>
    public IReadOnlyList<string> DeleteFor(IEnumerable<string> nodeIds)
    {
        var failed = new List<string>();
        foreach (var id in nodeIds)
        {
            foreach (var definition in SecretDefinitions)
            {
                var target = Target(id, definition.Key);
                try
                {
                    Store.Delete(target);
                }
                catch (CredentialStoreException)
                {
                    failed.Add(target);
                }
            }
        }

        return failed;
    }

    private static IEnumerable<TreeNode> SelfAndDescendants(TreeNode node)
    {
        yield return node;
        if (node is FolderNode folder)
        {
            foreach (var descendant in folder.Children.SelectMany(SelfAndDescendants))
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>
/// Credential store changes made ahead of a connection-file save: <see cref="Rollback"/> restores the previous
/// entries if the file save fails; <see cref="Commit"/> then makes the deferred removals.
/// </summary>
public sealed class SecretChange
{
    private readonly ICredentialStore _store;
    private readonly List<(string Target, string? Previous, string Comment)> _written = [];
    private readonly List<string> _deletes = [];

    internal SecretChange(ICredentialStore store) => _store = store;

    internal void Write(string target, string secret, string comment)
    {
        var previous = _store.Read(target);
        _store.Write(target, secret, comment);
        _written.Add((target, previous, comment));
    }

    internal void DeleteOnCommit(string target) => _deletes.Add(target);

    /// <summary>Restores the entries as they were before. Best effort: returns the targets that could not be restored.</summary>
    public IReadOnlyList<string> Rollback()
    {
        var failed = new List<string>();
        for (var i = _written.Count - 1; i >= 0; i--)
        {
            var (target, previous, comment) = _written[i];
            try
            {
                if (previous is null)
                {
                    _store.Delete(target);
                }
                else
                {
                    _store.Write(target, previous, comment);
                }
            }
            catch (CredentialStoreException)
            {
                failed.Add(target);
            }
        }

        _written.Clear();
        _deletes.Clear();
        return failed;
    }

    /// <summary>Makes the deferred removals. Returns the targets that could not be removed.</summary>
    public IReadOnlyList<string> Commit()
    {
        var failed = new List<string>();
        foreach (var target in _deletes)
        {
            try
            {
                _store.Delete(target);
            }
            catch (CredentialStoreException)
            {
                failed.Add(target);
            }
        }

        _written.Clear();
        _deletes.Clear();
        return failed;
    }
}
