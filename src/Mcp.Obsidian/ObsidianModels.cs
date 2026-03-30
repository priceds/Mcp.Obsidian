using System.Net;
using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal enum ObsidianReadFormat
{
    Markdown,
    NoteJson,
    DocumentMap,
}

internal sealed record ObsidianResource(
    string Scope,
    string? Path = null,
    string? Period = null,
    DateOnly? Date = null);

internal sealed record ObsidianPatchRequest(
    string Operation,
    string TargetType,
    string Target,
    JsonNode? Content,
    string ContentType,
    string Delimiter,
    bool TrimTargetWhitespace,
    bool CreateTargetIfMissing);

internal sealed record ObsidianSearchQuery(
    string Language,
    string? TextQuery,
    JsonNode? JsonLogicQuery);

internal sealed record ObsidianWorkspaceScaffoldRequest(
    string RootFolder,
    IReadOnlyList<string> Folders,
    bool CreateIndexNotes,
    string IndexNoteName,
    string WelcomeNoteName,
    bool IncludeDailyNotesFolder);

internal sealed record TagInfo(string Tag, int Count);

internal sealed record MoveResult(
    string From,
    string To,
    bool UpdatedLinks,
    int UpdatedNoteCount);

internal sealed record RecentNoteInfo(
    string Path,
    long Size,
    DateTimeOffset ModifiedAt);

internal sealed record VaultStats(
    int NoteCount,
    int FolderCount,
    long TotalSizeBytes,
    int TagCount,
    int OrphanCount,
    IReadOnlyList<RecentNoteInfo> RecentlyModified);

internal sealed record NoteInfo(
    string Path,
    long Size,
    DateTimeOffset ModifiedAt,
    JsonNode? Frontmatter,
    string? Content);

internal sealed record TaskItem(
    string SourcePath,
    int Line,
    string Text,
    bool Completed,
    DateOnly? DueDate,
    string? Priority,
    IReadOnlyList<string> Tags);

internal sealed record BrokenLink(
    string SourcePath,
    string BrokenTarget,
    int? LineNumber);

internal sealed record GraphNode(
    string Path,
    string Title,
    int Depth,
    string? Snippet);

internal sealed record GraphEdge(
    string FromPath,
    string ToPath);

internal sealed record GraphResult(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    int MaxDepthReached);

internal sealed record CanvasNode(
    string Id,
    string Type,
    JsonObject Data);

internal sealed record CanvasEdge(
    string Id,
    string? FromNode,
    string? ToNode,
    JsonObject Data);

internal sealed record CanvasData(
    IReadOnlyList<CanvasNode> Nodes,
    IReadOnlyList<CanvasEdge> Edges);

internal sealed record KanbanCard(string Text, bool Completed);

internal sealed record KanbanColumn(string Name, IReadOnlyList<KanbanCard> Cards);

internal sealed record KanbanBoard(
    string Path,
    IReadOnlyList<KanbanColumn> Columns);

internal sealed record DuplicateTitleInfo(
    string Title,
    IReadOnlyList<string> Paths);

internal sealed record LargeFileInfo(
    string Path,
    long Size);

internal sealed record HealthReport(
    VaultStats Stats,
    IReadOnlyList<BrokenLink> BrokenLinks,
    IReadOnlyList<DuplicateTitleInfo> DuplicateTitles,
    IReadOnlyList<string> OrphanNotes,
    IReadOnlyList<LargeFileInfo> LargeFiles,
    IReadOnlyList<TagInfo> Tags);

internal sealed record SemanticResult(
    string Path,
    string Title,
    float Score,
    string Snippet,
    int ChunkIndex,
    IReadOnlyList<string> MatchedTerms);

internal sealed record BulkNoteResult(
    string Path,
    int UpdatedFieldCount,
    IReadOnlyList<string> UpdatedFields);

internal sealed record BulkResult(
    int MatchedNoteCount,
    int UpdatedNoteCount,
    IReadOnlyList<BulkNoteResult> Notes);

internal sealed record SemanticChunkInput(
    string Path,
    int ChunkIndex,
    string Text,
    string Snippet);

internal sealed record NoteSnapshot(
    string Path,
    string Content,
    JsonNode? Frontmatter,
    long Size,
    DateTimeOffset ModifiedAt,
    IReadOnlyList<string> Tags);

internal sealed record VaultEntry(string Path, bool IsDirectory);

internal sealed class ObsidianApiException(HttpStatusCode statusCode, string responseBody)
    : Exception($"Obsidian API request failed with {(int)statusCode} {statusCode}. {responseBody}".Trim())
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string ResponseBody { get; } = responseBody;
}
