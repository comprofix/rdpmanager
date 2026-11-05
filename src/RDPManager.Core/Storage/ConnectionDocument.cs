using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RDPManager.Core.Model;

namespace RDPManager.Core.Storage;

/// <summary>The on-disk shape of <c>connections.json</c>. Unknown properties are kept and written back.</summary>
internal sealed class ConnectionDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("defaults")]
    public JsonObject? Defaults { get; set; }

    [JsonPropertyName("nodes")]
    public List<NodeDocument>? Nodes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public static ConnectionDocument FromTree(ConnectionTree tree) => new()
    {
        SchemaVersion = tree.SchemaVersion,
        Defaults = tree.Defaults,
        Nodes = tree.Roots.Select(NodeDocument.FromNode).ToList(),
        Unknown = tree.UnknownProperties,
    };

    public ConnectionTree ToTree(string path)
    {
        var tree = new ConnectionTree
        {
            SchemaVersion = SchemaVersion,
            Defaults = Defaults ?? [],
            UnknownProperties = Unknown,
        };

        foreach (var node in Nodes ?? [])
        {
            Add(tree, node, parentId: null, path);
        }

        return tree;
    }

    private static void Add(ConnectionTree tree, NodeDocument document, string? parentId, string path)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            throw new InvalidConnectionFileException(path, "a node has no id.");
        }

        TreeNode node = document.Type switch
        {
            NodeDocument.FolderType => new FolderNode(document.Id),
            NodeDocument.ConnectionType => new ConnectionNode(document.Id),
            _ => throw new InvalidConnectionFileException(path, $"node '{document.Id}' has unknown type '{document.Type}'."),
        };

        node.Name = document.Name ?? string.Empty;
        node.Modified = document.Modified;
        node.Revision = document.Revision;
        node.Settings = document.Settings ?? [];
        node.UnknownProperties = document.Unknown;

        try
        {
            tree.Add(node, parentId);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidConnectionFileException(path, ex.Message, ex);
        }

        if (document.Children is { Count: > 0 })
        {
            if (node is not FolderNode)
            {
                throw new InvalidConnectionFileException(path, $"connection '{document.Id}' has children.");
            }

            foreach (var child in document.Children)
            {
                Add(tree, child, node.Id, path);
            }
        }
    }
}

/// <summary>The on-disk shape of one folder or connection.</summary>
internal sealed class NodeDocument
{
    public const string FolderType = "folder";
    public const string ConnectionType = "connection";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("modified")]
    public DateTimeOffset Modified { get; set; }

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("settings")]
    public JsonObject? Settings { get; set; }

    [JsonPropertyName("children")]
    public List<NodeDocument>? Children { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public static NodeDocument FromNode(TreeNode node) => new()
    {
        Id = node.Id,
        Type = node.Type == NodeType.Folder ? FolderType : ConnectionType,
        Name = node.Name,
        Modified = node.Modified,
        Revision = node.Revision,
        Settings = node.Settings,
        Children = node is FolderNode folder ? folder.Children.Select(FromNode).ToList() : null,
        Unknown = node.UnknownProperties,
    };
}
