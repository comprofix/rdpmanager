using RDPManager.Core.Model;

namespace RDPManager.Core.Storage;

/// <summary>
/// A source of connections (the personal JSON file now; shared databases later). Operations are
/// node-level so that a database source can implement them without rewriting everything; tree
/// operations apply immediately, one call each.
/// </summary>
public interface IConnectionStore
{
    /// <summary>Loads the tree. The returned tree is the store's live copy.</summary>
    Task<ConnectionTree> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the node (under <paramref name="parentId"/>, or at the top level) if it is new, including any nodes
    /// inside a new folder; or updates the existing node with the same id (name, settings and unknown properties).
    /// Position follows <see cref="NodeOrder"/>, so a renamed node moves to its new place.
    /// Updates <see cref="TreeNode.Modified"/> and <see cref="TreeNode.Revision"/>, then persists.
    /// </summary>
    Task SaveNodeAsync(TreeNode node, string? parentId = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a node and everything below it, then persists. Returns false if it did not exist.</summary>
    Task<bool> DeleteNodeAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Moves a node to another folder (or the top level), then persists. Position follows <see cref="NodeOrder"/>.</summary>
    Task MoveNodeAsync(string id, string? newParentId, CancellationToken cancellationToken = default);
}
