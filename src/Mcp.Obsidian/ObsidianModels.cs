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

internal sealed class ObsidianApiException(HttpStatusCode statusCode, string responseBody)
    : Exception($"Obsidian API request failed with {(int)statusCode} {statusCode}. {responseBody}".Trim())
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string ResponseBody { get; } = responseBody;
}
