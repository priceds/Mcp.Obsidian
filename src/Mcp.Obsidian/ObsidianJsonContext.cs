using System.Text.Json.Serialization;

namespace Mcp.Obsidian;

[JsonSerializable(typeof(TagInfo[]))]
[JsonSerializable(typeof(GraphResult))]
[JsonSerializable(typeof(GraphNode[]))]
[JsonSerializable(typeof(GraphEdge[]))]
[JsonSerializable(typeof(VaultStats))]
[JsonSerializable(typeof(HealthReport))]
[JsonSerializable(typeof(SemanticResult[]))]
[JsonSerializable(typeof(BulkResult))]
internal sealed partial class ObsidianJsonContext : JsonSerializerContext { }
