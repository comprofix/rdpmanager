using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RDPManager.Core.Model;

namespace RDPManager.Core.Storage;

/// <summary>
/// The personal connection tree in <c>connections.json</c>.
/// <list type="bullet">
/// <item>Safe save: writes <c>connections.json.tmp</c>, flushes it to disk, then replaces the original.</item>
/// <item>On the first load, copies the file to <c>backups\</c> and keeps the newest <see cref="MaxBackups"/>.</item>
/// <item>Refuses files with a newer format version, and files it cannot read; neither is ever overwritten.</item>
/// </list>
/// </summary>
public sealed class JsonFileStore : IConnectionStore
{
    public const string FileName = "connections.json";
    public const string BackupFolderName = "backups";
    public const int MaxBackups = 10;

    private const string BackupPrefix = "connections-";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TimeProvider _time;
    private ConnectionTree? _tree;
    private bool _backedUp;

    public JsonFileStore(string folder, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
        FilePath = Path.Combine(Folder, FileName);
        BackupFolder = Path.Combine(Folder, BackupFolderName);
        _time = timeProvider ?? TimeProvider.System;
    }

    public string Folder { get; }

    public string FilePath { get; }

    public string BackupFolder { get; }

    public async Task<ConnectionTree> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            _tree = new ConnectionTree();
            return _tree;
        }

        if (!_backedUp)
        {
            BackUp();
            _backedUp = true;
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(FilePath, cancellationToken);
        }
        catch (IOException ex)
        {
            throw new InvalidConnectionFileException(FilePath, ex.Message, ex);
        }

        _tree = Parse(text);
        return _tree;
    }

    public Task SaveNodeAsync(TreeNode node, string? parentId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var tree = RequireLoaded();

        var existing = tree.Find(node.Id);
        TreeNode target;
        if (existing is null)
        {
            tree.Add(node, parentId);
            target = node;
        }
        else
        {
            if (existing.Type != node.Type)
            {
                throw new InvalidOperationException($"Node '{node.Id}' is a {existing.Type}, not a {node.Type}.");
            }

            if (!ReferenceEquals(existing, node))
            {
                existing.Name = node.Name;
                existing.Settings = (JsonObject)node.Settings.DeepClone();
                existing.UnknownProperties = node.UnknownProperties;
            }

            target = existing;
        }

        Touch(target);
        return WriteAsync(tree, cancellationToken);
    }

    public async Task<bool> DeleteNodeAsync(string id, CancellationToken cancellationToken = default)
    {
        var tree = RequireLoaded();
        if (!tree.Remove(id))
        {
            return false;
        }

        await WriteAsync(tree, cancellationToken);
        return true;
    }

    public Task MoveNodeAsync(string id, string? newParentId, int index, CancellationToken cancellationToken = default)
    {
        var tree = RequireLoaded();
        tree.Move(id, newParentId, index);
        Touch(tree.Find(id)!);
        return WriteAsync(tree, cancellationToken);
    }

    private ConnectionTree RequireLoaded() =>
        _tree ?? throw new InvalidOperationException("Call LoadAsync before changing the tree.");

    private void Touch(TreeNode node)
    {
        node.Revision++;
        node.Modified = _time.GetUtcNow();
    }

    private ConnectionTree Parse(string text)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidConnectionFileException(FilePath, "the file does not contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidConnectionFileException(FilePath, $"it is not valid JSON ({ex.Message}).", ex);
        }

        // Check the version before reading anything else: a newer format may not fit this shape.
        int version;
        try
        {
            version = root["schemaVersion"]?.GetValue<int>() ?? 0;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new InvalidConnectionFileException(FilePath, "'schemaVersion' is not a number.", ex);
        }

        if (version > ConnectionTree.CurrentSchemaVersion)
        {
            throw new UnsupportedFileVersionException(FilePath, version, ConnectionTree.CurrentSchemaVersion);
        }

        if (version < 1)
        {
            throw new InvalidConnectionFileException(FilePath, "'schemaVersion' is missing or invalid.");
        }

        try
        {
            var document = root.Deserialize<ConnectionDocument>(SerializerOptions)
                ?? throw new InvalidConnectionFileException(FilePath, "the file is empty.");
            return document.ToTree(FilePath);
        }
        catch (JsonException ex)
        {
            throw new InvalidConnectionFileException(FilePath, ex.Message, ex);
        }
    }

    private async Task WriteAsync(ConnectionTree tree, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);
        var tempPath = FilePath + ".tmp";
        var document = ConnectionDocument.FromTree(tree);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, FilePath, overwrite: true);
    }

    private void BackUp()
    {
        Directory.CreateDirectory(BackupFolder);

        var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(BackupFolder, $"{BackupPrefix}{stamp}.json");
        for (var n = 1; File.Exists(backupPath); n++)
        {
            backupPath = Path.Combine(BackupFolder, $"{BackupPrefix}{stamp}-{n}.json");
        }

        File.Copy(FilePath, backupPath);

        // Names sort by time, so keep the newest MaxBackups.
        var stale = Directory.GetFiles(BackupFolder, $"{BackupPrefix}*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(MaxBackups);
        foreach (var file in stale)
        {
            File.Delete(file);
        }
    }
}
