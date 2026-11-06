namespace RDPManager.Core.Credentials;

/// <summary>A credential store held in memory, for tests and for platforms without a store yet.</summary>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, (string Secret, string Comment)> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The stored entries (target → secret), for tests.</summary>
    public IReadOnlyDictionary<string, string> Entries => _entries.ToDictionary(e => e.Key, e => e.Value.Secret);

    /// <summary>When set, the next write or delete to a target starting with this text fails (for tests).</summary>
    public string? FailFor { get; set; }

    public string? Read(string target) => _entries.TryGetValue(target, out var entry) ? entry.Secret : null;

    public void Write(string target, string secret, string comment)
    {
        ThrowIfFailing(target);
        _entries[target] = (secret, comment);
    }

    public bool Delete(string target)
    {
        ThrowIfFailing(target);
        return _entries.Remove(target);
    }

    public string? CommentFor(string target) => _entries.TryGetValue(target, out var entry) ? entry.Comment : null;

    private void ThrowIfFailing(string target)
    {
        if (FailFor is { } prefix && target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new CredentialStoreException($"Simulated failure for {target}.");
        }
    }
}
