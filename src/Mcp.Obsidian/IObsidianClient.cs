using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal interface IObsidianClient
{
    Task<JsonNode?> ReadResourceAsync(ObsidianResource resource, ObsidianReadFormat format, CancellationToken cancellationToken);

    Task<string> WriteResourceAsync(ObsidianResource resource, string content, CancellationToken cancellationToken);

    Task<string> AppendResourceAsync(ObsidianResource resource, string content, CancellationToken cancellationToken);

    Task DeleteResourceAsync(ObsidianResource resource, CancellationToken cancellationToken);

    Task<string> PatchResourceAsync(ObsidianResource resource, ObsidianPatchRequest patch, CancellationToken cancellationToken);

    Task<JsonNode?> SearchSimpleAsync(string query, int? contextLength, CancellationToken cancellationToken);

    Task<JsonNode?> QueryVaultAsync(ObsidianSearchQuery query, CancellationToken cancellationToken);

    Task<JsonNode?> ListFilesAsync(string? folder, CancellationToken cancellationToken);

    Task<JsonNode?> ListCommandsAsync(CancellationToken cancellationToken);

    Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken);

    Task OpenNoteAsync(string path, bool newLeaf, CancellationToken cancellationToken);
}
