namespace RDPManager.Core.Storage;

/// <summary>The connection file could not be read (invalid JSON or an invalid structure). It is never overwritten.</summary>
public class InvalidConnectionFileException(string path, string message, Exception? innerException = null)
    : Exception($"The connection file '{path}' could not be read: {message}", innerException)
{
    public string FilePath { get; } = path;
}

/// <summary>The connection file was written by a newer RDPManager. It is left unchanged.</summary>
public sealed class UnsupportedFileVersionException(string path, int foundVersion, int supportedVersion)
    : InvalidConnectionFileException(
        path,
        $"it uses format version {foundVersion}, but this version of RDPManager supports up to version {supportedVersion}. " +
        "Use a newer RDPManager to open it.")
{
    public int FoundVersion { get; } = foundVersion;

    public int SupportedVersion { get; } = supportedVersion;
}
