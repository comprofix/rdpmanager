namespace RDPManager.Core.Credentials;

/// <summary>
/// The operating system's credential store (DESIGN: Credentials). Windows uses Credential Manager;
/// Linux will use the Secret Service API. Entries are addressed by a target name.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Returns the stored secret, or null when there is no entry.</summary>
    /// <exception cref="CredentialStoreException">The store could not be read.</exception>
    string? Read(string target);

    /// <summary>Creates or replaces an entry. <paramref name="comment"/> helps people recognise it in the store's own tools.</summary>
    /// <exception cref="CredentialStoreException">The entry could not be written.</exception>
    void Write(string target, string secret, string comment);

    /// <summary>Removes an entry. Returns false when there was none.</summary>
    /// <exception cref="CredentialStoreException">The entry could not be removed.</exception>
    bool Delete(string target);
}

/// <summary>The credential store refused a read, write or delete.</summary>
public sealed class CredentialStoreException(string message, Exception? inner = null) : Exception(message, inner);
