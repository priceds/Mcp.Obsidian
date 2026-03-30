using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mcp.Obsidian;

internal sealed partial class ObsidianVaultService
{
    private readonly IObsidianClient _client;
    private readonly SemanticSearchService _semanticSearch;

    public ObsidianVaultService(IObsidianClient client, ObsidianSettings settings)
    {
        _client = client;
        _semanticSearch = new SemanticSearchService(settings.SemanticSearch);
    }

    public async Task<IReadOnlyList<TagInfo>> ListAllTagsAsync(CancellationToken cancellationToken)
    {
        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken);
        var tags = BuildTagIndex(snapshots);
        return tags
            .Select(entry => new TagInfo(entry.Key, entry.Value))
            .OrderByDescending(static entry => entry.Count)
            .ThenBy(static entry => entry.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<MoveResult> MoveNoteAsync(string from, string to, bool updateLinks, CancellationToken cancellationToken)
    {
        var markdown = (await _client.ReadResourceAsync(new ObsidianResource("vault", from), ObsidianReadFormat.Markdown, cancellationToken))?.GetValue<string>()
                       ?? throw new InvalidOperationException($"Could not read note '{from}'.");
        string[] notePaths = [];

        if (updateLinks)
        {
            notePaths = (await ListVaultNotePathsAsync(cancellationToken))
                .Where(path => !string.Equals(path, from, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        await _client.WriteResourceAsync(new ObsidianResource("vault", to), markdown, cancellationToken);

        var updatedNotes = 0;
        if (updateLinks)
        {
            await Parallel.ForEachAsync(notePaths, cancellationToken, async (notePath, token) =>
            {
                var content = (await _client.ReadResourceAsync(new ObsidianResource("vault", notePath), ObsidianReadFormat.Markdown, token))?.GetValue<string>() ?? string.Empty;
                var rewritten = ObsidianMarkdownTools.RewriteWikiLinks(content, from, to);
                if (string.Equals(content, rewritten, StringComparison.Ordinal))
                {
                    return;
                }

                await _client.WriteResourceAsync(new ObsidianResource("vault", notePath), rewritten, token);
                Interlocked.Increment(ref updatedNotes);
            });
        }

        await _client.DeleteResourceAsync(new ObsidianResource("vault", from), cancellationToken);

        return new MoveResult(from, to, updateLinks, updatedNotes);
    }

    public async Task<VaultStats> GetVaultStatsAsync(int recentCount, CancellationToken cancellationToken)
    {
        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken);
        var folders = await ListVaultDirectoriesAsync(cancellationToken);
        var backlinks = BuildBacklinkCounts(snapshots);
        var tags = BuildTagIndex(snapshots);

        return new VaultStats(
            snapshots.Count,
            folders.Count,
            snapshots.Sum(static item => item.Size),
            tags.Count,
            snapshots.Count(snapshot => (backlinks.GetValueOrDefault(snapshot.Path, 0) == 0)),
            snapshots
                .OrderByDescending(static item => item.ModifiedAt)
                .Take(Math.Max(1, recentCount))
                .Select(static item => new RecentNoteInfo(item.Path, item.Size, item.ModifiedAt))
                .ToArray());
    }

    public async Task<IReadOnlyList<NoteInfo>> BatchReadAsync(
        IReadOnlyList<string> paths,
        bool includeContent,
        bool includeFrontmatter,
        CancellationToken cancellationToken)
    {
        var distinctPaths = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tasks = distinctPaths.Select(path => ReadSingleNoteInfoAsync(path, includeContent, includeFrontmatter, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<TaskItem>> ExtractTasksAsync(string? folder, bool? completed, CancellationToken cancellationToken)
    {
        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken, folder);
        return snapshots
            .SelectMany(snapshot => ObsidianMarkdownTools.ExtractTasks(snapshot.Path, snapshot.Content))
            .Where(task => completed is null || task.Completed == completed.Value)
            .OrderBy(static task => task.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static task => task.Line)
            .ToArray();
    }

    public async Task<IReadOnlyList<BrokenLink>> ListBrokenLinksAsync(CancellationToken cancellationToken)
    {
        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken);
        var existingTargets = snapshots
            .Select(static note => Path.GetFileNameWithoutExtension(note.Path).Trim().ToLowerInvariant())
            .Where(static item => item.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return snapshots
            .SelectMany(snapshot => ObsidianMarkdownTools.FindBrokenLinks(snapshot.Path, snapshot.Content, existingTargets))
            .OrderBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.LineNumber)
            .ThenBy(static item => item.BrokenTarget, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<GraphResult> GraphTraverseAsync(
        string startNote,
        int maxDepth,
        string direction,
        bool includeSnippet,
        CancellationToken cancellationToken)
    {
        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken);
        var titleIndex = BuildTitleIndex(snapshots);
        var contentIndex = snapshots.ToDictionary(static snapshot => snapshot.Path, static snapshot => snapshot, StringComparer.OrdinalIgnoreCase);

        var outgoing = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in ObsidianMarkdownTools.ExtractWikiLinks(snapshot.Content))
            {
                if (titleIndex.TryGetValue(link, out var targetPath))
                {
                    resolved.Add(targetPath);
                    continue;
                }

                var match = snapshots.FirstOrDefault(candidate =>
                    candidate.Path.EndsWith($"{link}.md", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Path.EndsWith(link, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    resolved.Add(match.Path);
                }
            }

            outgoing[snapshot.Path] = resolved;
        }

        var incoming = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, targets) in outgoing)
        {
            foreach (var to in targets)
            {
                if (!incoming.TryGetValue(to, out var set))
                {
                    incoming[to] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                set.Add(from);
            }
        }

        string? startPath = null;
        if (contentIndex.ContainsKey(startNote))
        {
            startPath = startNote;
        }
        else if (titleIndex.TryGetValue(startNote, out var byTitle))
        {
            startPath = byTitle;
        }
        else
        {
            startPath = snapshots.FirstOrDefault(snapshot =>
                snapshot.Path.EndsWith($"{startNote}.md", StringComparison.OrdinalIgnoreCase) ||
                snapshot.Path.EndsWith(startNote, StringComparison.OrdinalIgnoreCase))?.Path;
        }

        if (startPath is null)
        {
            throw new InvalidOperationException($"Note not found: '{startNote}'");
        }

        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((startPath, 0));
        visited.Add(startPath);
        var maxReached = 0;

        while (queue.Count > 0 && nodes.Count < 500)
        {
            var (current, depth) = queue.Dequeue();
            maxReached = Math.Max(maxReached, depth);

            var snapshot = contentIndex.GetValueOrDefault(current);
            var title = snapshot is not null
                ? Path.GetFileNameWithoutExtension(snapshot.Path)
                : Path.GetFileNameWithoutExtension(current);
            var snippet = includeSnippet && snapshot is not null
                ? snapshot.Content.Length > 200 ? snapshot.Content[..200] : snapshot.Content
                : null;

            nodes.Add(new GraphNode(current, title, depth, snippet));
            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> neighbors = direction switch
            {
                "outgoing" => outgoing.GetValueOrDefault(current) ?? [],
                "incoming" => incoming.GetValueOrDefault(current) ?? [],
                _ => (outgoing.GetValueOrDefault(current) ?? []).Concat(incoming.GetValueOrDefault(current) ?? []),
            };

            foreach (var neighbor in neighbors)
            {
                if (visited.Contains(neighbor))
                {
                    continue;
                }

                visited.Add(neighbor);
                queue.Enqueue((neighbor, depth + 1));
                edges.Add(direction == "incoming"
                    ? new GraphEdge(neighbor, current)
                    : new GraphEdge(current, neighbor));
            }
        }

        return new GraphResult(nodes, edges, maxReached);
    }

    public async Task<CanvasData> ReadCanvasAsync(string path, CancellationToken cancellationToken)
    {
        var markdown = (await _client.ReadResourceAsync(new ObsidianResource("vault", path), ObsidianReadFormat.Markdown, cancellationToken))?.GetValue<string>()
                       ?? throw new InvalidOperationException($"Could not read canvas '{path}'.");
        var root = JsonNode.Parse(markdown) as JsonObject
                   ?? throw new InvalidOperationException($"Canvas '{path}' does not contain valid JSON.");
        var nodes = (root["nodes"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(static node => new CanvasNode(
                node["id"]?.GetValue<string>() ?? string.Empty,
                node["type"]?.GetValue<string>() ?? "unknown",
                node.DeepClone().AsObject()))
            .ToArray();
        var edges = (root["edges"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(static edge => new CanvasEdge(
                edge["id"]?.GetValue<string>() ?? string.Empty,
                edge["fromNode"]?.GetValue<string>(),
                edge["toNode"]?.GetValue<string>(),
                edge.DeepClone().AsObject()))
            .ToArray();

        return new CanvasData(nodes, edges);
    }

    public async Task<KanbanBoard> ReadKanbanAsync(string path, CancellationToken cancellationToken)
    {
        var markdown = (await _client.ReadResourceAsync(
                new ObsidianResource("vault", path),
                ObsidianReadFormat.Markdown,
                cancellationToken))
            ?.GetValue<string>() ?? throw new InvalidOperationException($"Could not read '{path}'.");

        var columns = new List<KanbanColumn>();
        KanbanColumn? current = null;
        var cards = new List<KanbanCard>();

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("%% kanban:settings", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    columns.Add(current with { Cards = cards.ToArray() });
                }

                current = new KanbanColumn(line[3..].Trim(), []);
                cards = [];
                continue;
            }

            if (current is not null && line.StartsWith("- [", StringComparison.Ordinal) && line.Length > 6)
            {
                var completed = line[3] is 'x' or 'X';
                var text = line[6..].Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    cards.Add(new KanbanCard(text, completed));
                }
            }
        }

        if (current is not null)
        {
            columns.Add(current with { Cards = cards.ToArray() });
        }

        return new KanbanBoard(path, columns);
    }

    public async Task<HealthReport> GetVaultHealthAsync(CancellationToken cancellationToken)
    {
        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken);
        var stats = await GetVaultStatsAsync(10, cancellationToken);
        var brokenLinks = await ListBrokenLinksAsync(cancellationToken);
        var backlinks = BuildBacklinkCounts(snapshots);
        var tags = BuildTagIndex(snapshots)
            .Select(entry => new TagInfo(entry.Key, entry.Value))
            .OrderByDescending(static entry => entry.Count)
            .ThenBy(static entry => entry.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicateTitles = snapshots
            .GroupBy(static snapshot => Path.GetFileNameWithoutExtension(snapshot.Path), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DuplicateTitleInfo(group.Key, group.Select(static snapshot => snapshot.Path).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();

        var orphanNotes = snapshots
            .Where(snapshot => backlinks.GetValueOrDefault(snapshot.Path, 0) == 0)
            .Select(static snapshot => snapshot.Path)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var largeFiles = snapshots
            .OrderByDescending(static snapshot => snapshot.Size)
            .Take(10)
            .Select(static snapshot => new LargeFileInfo(snapshot.Path, snapshot.Size))
            .ToArray();

        return new HealthReport(stats, brokenLinks, duplicateTitles, orphanNotes, largeFiles, tags);
    }

    public async Task<IReadOnlyList<SemanticResult>> SearchSemanticAsync(string query, int limit, float minScore, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("A search query is required.");
        }

        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken);
        if (snapshots.Count == 0)
        {
            return [];
        }

        var queryTerms = Tokenize(query);
        var normalizedQuery = NormalizeSemanticText(query);
        if (queryTerms.Count == 0 && normalizedQuery.Length == 0)
        {
            throw new InvalidOperationException("The query did not contain any searchable terms.");
        }

        var chunks = snapshots
            .SelectMany(BuildSemanticChunks)
            .Select(static (chunk, index) => chunk with { GlobalIndex = index })
            .ToArray();
        if (chunks.Length == 0)
        {
            return [];
        }

        var averageChunkLength = chunks.Length == 0
            ? 1d
            : chunks.Average(static chunk => Math.Max(1, chunk.TokenCount));
        var documentFrequencies = BuildDocumentFrequencies(chunks);
        var chunkInputs = chunks
            .Select(static chunk => new SemanticChunkInput(chunk.Path, chunk.Index, chunk.Text, chunk.Text.Length > 200 ? chunk.Text[..200] : chunk.Text))
            .ToArray();
        var embeddingScores = await _semanticSearch.ScoreChunksAsync(query, chunkInputs, cancellationToken);
        var results = new List<SemanticResult>();

        foreach (var noteGroup in chunks.GroupBy(static chunk => chunk.Path, StringComparer.OrdinalIgnoreCase))
        {
            SemanticChunk? bestChunk = null;
            var bestScore = 0d;
            HashSet<string> bestTerms = [];

            foreach (var chunk in noteGroup)
            {
                var score = ScoreChunk(chunk, queryTerms, normalizedQuery, documentFrequencies, chunks.Length, averageChunkLength, out var matchedTerms);
                if (embeddingScores is not null)
                {
                    var embeddingScore = embeddingScores[chunk.GlobalIndex];
                    score = (0.65d * embeddingScore) + (0.35d * score);
                }

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestChunk = chunk;
                bestTerms = matchedTerms;
            }

            if (bestChunk is null || bestScore < minScore)
            {
                continue;
            }

            results.Add(new SemanticResult(
                bestChunk.Path,
                Path.GetFileNameWithoutExtension(bestChunk.Path),
                (float)Math.Round(bestScore, 4, MidpointRounding.AwayFromZero),
                BuildSnippet(bestChunk.Text, bestTerms, queryTerms),
                bestChunk.Index,
                bestTerms.OrderBy(static term => term, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        return results
            .OrderByDescending(static result => result.Score)
            .ThenBy(static result => result.Path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    public async Task<BulkResult> BulkFrontmatterAsync(string? folder, string? tag, JsonObject updates, CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("At least one frontmatter update is required.");
        }

        var normalizedTag = NormalizeTagFilter(tag);
        var snapshots = await ReadVaultSnapshotsAsync(cancellationToken, folder);
        var matchingSnapshots = snapshots
            .Where(snapshot => normalizedTag is null || snapshot.Tags.Contains(normalizedTag, StringComparer.OrdinalIgnoreCase))
            .OrderBy(static snapshot => snapshot.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var updatedNotes = new List<BulkNoteResult>(matchingSnapshots.Length);
        foreach (var snapshot in matchingSnapshots)
        {
            var updatedFields = new List<string>();
            foreach (var update in updates)
            {
                if (string.IsNullOrWhiteSpace(update.Key) || update.Value is null)
                {
                    continue;
                }

                await _client.PatchResourceAsync(
                    new ObsidianResource("vault", snapshot.Path),
                    new ObsidianPatchRequest("replace", "frontmatter", update.Key, update.Value.DeepClone(), "application/json", "::", false, true),
                    cancellationToken);
                updatedFields.Add(update.Key);
            }

            if (updatedFields.Count == 0)
            {
                continue;
            }

            updatedNotes.Add(new BulkNoteResult(snapshot.Path, updatedFields.Count, updatedFields));
        }

        return new BulkResult(
            matchingSnapshots.Length,
            updatedNotes.Count,
            updatedNotes);
    }

    private async Task<NoteInfo> ReadSingleNoteInfoAsync(
        string path,
        bool includeContent,
        bool includeFrontmatter,
        CancellationToken cancellationToken)
    {
        var noteJson = await _client.ReadResourceAsync(new ObsidianResource("vault", path), ObsidianReadFormat.NoteJson, cancellationToken) as JsonObject
                       ?? throw new InvalidOperationException($"Could not read note metadata for '{path}'.");
        var frontmatter = includeFrontmatter ? noteJson["frontmatter"]?.DeepClone() : null;
        string? content = null;

        if (includeContent)
        {
            content = noteJson["content"]?.GetValue<string>()
                      ?? (await _client.ReadResourceAsync(new ObsidianResource("vault", path), ObsidianReadFormat.Markdown, cancellationToken))?.GetValue<string>();
        }

        return new NoteInfo(
            noteJson["path"]?.GetValue<string>() ?? path,
            noteJson["stat"]?["size"]?.GetValue<long>() ?? (content?.Length ?? 0),
            ToDateTimeOffset(noteJson["stat"]?["mtime"]?.GetValue<long>()),
            frontmatter,
            content);
    }

    private async Task<IReadOnlyList<NoteSnapshot>> ReadVaultSnapshotsAsync(CancellationToken cancellationToken, string? folder = null)
    {
        var notePaths = await ListVaultNotePathsAsync(cancellationToken, folder);
        var bag = new ConcurrentBag<NoteSnapshot>();

        await Parallel.ForEachAsync(notePaths, cancellationToken, async (path, token) =>
        {
            var noteJson = await _client.ReadResourceAsync(new ObsidianResource("vault", path), ObsidianReadFormat.NoteJson, token) as JsonObject
                           ?? throw new InvalidOperationException($"Could not read note metadata for '{path}'.");
            var content = noteJson["content"]?.GetValue<string>()
                          ?? (await _client.ReadResourceAsync(new ObsidianResource("vault", path), ObsidianReadFormat.Markdown, token))?.GetValue<string>()
                          ?? string.Empty;
            var frontmatter = noteJson["frontmatter"]?.DeepClone();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in ObsidianMarkdownTools.ExtractFrontmatterTags(frontmatter))
            {
                tags.Add(tag);
            }

            foreach (var tag in ObsidianMarkdownTools.ExtractInlineTags(content))
            {
                tags.Add(tag);
            }

            bag.Add(new NoteSnapshot(
                noteJson["path"]?.GetValue<string>() ?? path,
                content,
                frontmatter,
                noteJson["stat"]?["size"]?.GetValue<long>() ?? content.Length,
                ToDateTimeOffset(noteJson["stat"]?["mtime"]?.GetValue<long>()),
                tags.OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase).ToArray()));
        });

        return bag.OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<string>> ListVaultNotePathsAsync(CancellationToken cancellationToken, string? rootFolder = null)
    {
        var entries = await ListVaultEntriesRecursiveAsync(rootFolder, cancellationToken);
        return entries
            .Where(static entry => !entry.IsDirectory && entry.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(static entry => entry.Path)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> ListVaultDirectoriesAsync(CancellationToken cancellationToken)
    {
        var entries = await ListVaultEntriesRecursiveAsync(null, cancellationToken);
        return entries
            .Where(static entry => entry.IsDirectory)
            .Select(static entry => entry.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<VaultEntry>> ListVaultEntriesRecursiveAsync(string? rootFolder, CancellationToken cancellationToken)
    {
        var pendingFolders = new Queue<string?>();
        var visitedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<VaultEntry>();
        pendingFolders.Enqueue(string.IsNullOrWhiteSpace(rootFolder) ? null : rootFolder.Trim().Trim('/'));

        while (pendingFolders.Count > 0)
        {
            var folder = pendingFolders.Dequeue();
            var normalizedFolder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim().Trim('/');
            var dedupeKey = normalizedFolder ?? "/";
            if (!visitedFolders.Add(dedupeKey))
            {
                continue;
            }

            var listing = await _client.ListFilesAsync(normalizedFolder, cancellationToken);
            foreach (var item in ParseListing(normalizedFolder, listing))
            {
                entries.Add(item);
                if (item.IsDirectory)
                {
                    pendingFolders.Enqueue(item.Path);
                }
            }
        }

        return entries;
    }

    private static IReadOnlyList<VaultEntry> ParseListing(string? folder, JsonNode? listing)
    {
        var results = new List<VaultEntry>();
        if (listing is JsonArray array)
        {
            foreach (var item in array)
            {
                AddEntry(results, folder, item);
            }

            return results;
        }

        if (listing is JsonObject obj)
        {
            if (obj["files"] is JsonArray files)
            {
                foreach (var item in files)
                {
                    AddEntry(results, folder, item);
                }
            }
            else
            {
                foreach (var property in obj)
                {
                    AddEntry(results, folder, property.Value);
                }
            }
        }

        return results;
    }

    private static void AddEntry(List<VaultEntry> results, string? folder, JsonNode? item)
    {
        switch (item)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
            {
                var normalized = NormalizeListedPath(folder, text);
                var isDirectory = normalized.EndsWith("/", StringComparison.Ordinal);
                results.Add(new VaultEntry(isDirectory ? normalized.TrimEnd('/') : normalized, isDirectory));
                break;
            }
            case JsonObject obj:
            {
                var candidatePath = obj["path"]?.GetValue<string>()
                                    ?? obj["name"]?.GetValue<string>()
                                    ?? obj["filename"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(candidatePath))
                {
                    break;
                }

                var normalized = NormalizeListedPath(folder, candidatePath);
                var isDirectory = obj["type"]?.GetValue<string>()?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true ||
                                  obj["isDirectory"]?.GetValue<bool>() == true ||
                                  normalized.EndsWith("/", StringComparison.Ordinal);
                results.Add(new VaultEntry(isDirectory ? normalized.TrimEnd('/') : normalized, isDirectory));
                break;
            }
        }
    }

    private static string NormalizeListedPath(string? folder, string item)
    {
        var trimmed = item.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimStart('/');
        }

        var baseFolder = string.IsNullOrWhiteSpace(folder) ? string.Empty : $"{folder.Trim().Trim('/')}/";
        var combined = trimmed.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(baseFolder)
            ? trimmed
            : $"{baseFolder}{trimmed}";

        return combined.Replace('\\', '/').Trim();
    }

    private static Dictionary<string, int> BuildTagIndex(IEnumerable<NoteSnapshot> snapshots)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            foreach (var tag in snapshot.Tags)
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        return counts;
    }

    private static Dictionary<string, int> BuildBacklinkCounts(IEnumerable<NoteSnapshot> snapshots)
    {
        var targets = snapshots
            .GroupBy(static snapshot => Path.GetFileNameWithoutExtension(snapshot.Path).Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(
                static group => group.Key.ToLowerInvariant(),
                static group => group.Single().Path,
                StringComparer.OrdinalIgnoreCase);
        var backlinks = snapshots.ToDictionary(static snapshot => snapshot.Path, static _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in snapshots)
        {
            foreach (var link in ObsidianMarkdownTools.ExtractLinks(snapshot.Content).OfType<JsonObject>())
            {
                if (link["type"]?.GetValue<string>() != "wikilink")
                {
                    continue;
                }

                var normalizedTarget = link["normalizedTarget"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(normalizedTarget))
                {
                    continue;
                }

                if (targets.TryGetValue(normalizedTarget, out var resolvedPath))
                {
                    backlinks[resolvedPath] = backlinks.GetValueOrDefault(resolvedPath) + 1;
                }
            }
        }

        return backlinks;
    }

    private static Dictionary<string, int> BuildDocumentFrequencies(IEnumerable<SemanticChunk> chunks)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in chunks)
        {
            foreach (var term in chunk.TermFrequencies.Keys)
            {
                counts[term] = counts.GetValueOrDefault(term) + 1;
            }
        }

        return counts;
    }

    private static Dictionary<string, string> BuildTitleIndex(IEnumerable<NoteSnapshot> snapshots)
    {
        return snapshots
            .GroupBy(static snapshot => Path.GetFileNameWithoutExtension(snapshot.Path).Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single().Path,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<SemanticChunk> BuildSemanticChunks(NoteSnapshot snapshot)
    {
        var blocks = snapshot.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var working = new List<string>();
        var workingTokens = 0;
        var chunkIndex = 0;

        foreach (var block in blocks)
        {
            var normalizedBlock = block.Trim();
            if (normalizedBlock.Length == 0)
            {
                continue;
            }

            var blockTokens = Tokenize(normalizedBlock);
            if (blockTokens.Count == 0)
            {
                continue;
            }

            if (workingTokens > 0 && workingTokens + blockTokens.Count > 140)
            {
                yield return CreateSemanticChunk(snapshot, chunkIndex++, working);
                working.Clear();
                workingTokens = 0;
            }

            working.Add(normalizedBlock);
            workingTokens += blockTokens.Count;
        }

        if (working.Count > 0)
        {
            yield return CreateSemanticChunk(snapshot, chunkIndex, working);
        }
    }

    private static SemanticChunk CreateSemanticChunk(NoteSnapshot snapshot, int index, IReadOnlyList<string> blocks)
    {
        var text = string.Join("\n\n", blocks);
        var terms = Tokenize(text);
        var termFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            termFrequencies[term] = termFrequencies.GetValueOrDefault(term) + 1;
        }

        var titleTerms = Tokenize(Path.GetFileNameWithoutExtension(snapshot.Path));
        var frontmatterTerms = ExtractFrontmatterTerms(snapshot.Frontmatter);

        return new SemanticChunk(
            snapshot.Path,
            index,
            0,
            text,
            terms.Count,
            termFrequencies,
            titleTerms.ToHashSet(StringComparer.OrdinalIgnoreCase),
            snapshot.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase),
            frontmatterTerms.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static double ScoreChunk(
        SemanticChunk chunk,
        IReadOnlyCollection<string> queryTerms,
        string normalizedQuery,
        IReadOnlyDictionary<string, int> documentFrequencies,
        int chunkCount,
        double averageChunkLength,
        out HashSet<string> matchedTerms)
    {
        matchedTerms = [];
        if (queryTerms.Count == 0 && normalizedQuery.Length == 0)
        {
            return 0d;
        }

        var bm25 = 0d;
        foreach (var term in queryTerms)
        {
            if (!chunk.TermFrequencies.TryGetValue(term, out var tf))
            {
                continue;
            }

            matchedTerms.Add(term);
            var df = documentFrequencies.GetValueOrDefault(term, 0);
            var idf = Math.Log(1d + ((chunkCount - df + 0.5d) / (df + 0.5d)));
            const double k1 = 1.5d;
            const double b = 0.75d;
            var denominator = tf + (k1 * (1d - b + (b * chunk.TokenCount / averageChunkLength)));
            bm25 += idf * ((tf * (k1 + 1d)) / Math.Max(1e-9, denominator));
        }

        var lexicalCoverage = queryTerms.Count == 0 ? 0d : matchedTerms.Count / (double)queryTerms.Count;
        var bm25Normalized = 1d - Math.Exp(-bm25 / 3d);
        var titleCoverage = queryTerms.Count == 0 ? 0d : matchedTerms.Count(term => chunk.TitleTerms.Contains(term)) / (double)queryTerms.Count;
        var tagCoverage = queryTerms.Count == 0 ? 0d : matchedTerms.Count(term => chunk.Tags.Contains(term) || chunk.FrontmatterTerms.Contains(term)) / (double)queryTerms.Count;
        var trigramSimilarity = ComputeDiceCoefficient(normalizedQuery, NormalizeSemanticText(chunk.Text));

        return (0.45d * lexicalCoverage) +
               (0.25d * bm25Normalized) +
               (0.15d * trigramSimilarity) +
               (0.10d * titleCoverage) +
               (0.05d * tagCoverage);
    }

    private static string BuildSnippet(string text, IReadOnlySet<string> matchedTerms, IReadOnlyCollection<string> queryTerms)
    {
        var compact = Regex.Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), @"\s+", " ").Trim();
        if (compact.Length <= 240)
        {
            return compact;
        }

        var anchor = matchedTerms
            .Concat(queryTerms)
            .FirstOrDefault(term => term.Length > 0 && compact.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (anchor is null)
        {
            return $"{compact[..237]}...";
        }

        var index = compact.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return $"{compact[..237]}...";
        }

        var start = Math.Max(0, index - 80);
        var length = Math.Min(240, compact.Length - start);
        var snippet = compact.Substring(start, length).Trim();
        if (start > 0)
        {
            snippet = $"...{snippet}";
        }

        if (start + length < compact.Length)
        {
            snippet = $"{snippet}...";
        }

        return snippet;
    }

    private static HashSet<string> ExtractFrontmatterTerms(JsonNode? frontmatter)
    {
        if (frontmatter is not JsonObject obj)
        {
            return [];
        }

        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj)
        {
            switch (property.Value)
            {
                case JsonValue value when value.TryGetValue<string>(out var text):
                    foreach (var term in Tokenize(text))
                    {
                        terms.Add(term);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item is JsonValue itemValue && itemValue.TryGetValue<string>(out var itemText))
                        {
                            foreach (var term in Tokenize(itemText))
                            {
                                terms.Add(term);
                            }
                        }
                    }
                    break;
            }
        }

        return terms;
    }

    private static List<string> Tokenize(string value)
    {
        return SemanticTokenRegex()
            .Matches(NormalizeSemanticText(value))
            .Select(static match => match.Value)
            .Where(static token => token.Length >= 2 && !SemanticStopWords.Contains(token))
            .ToList();
    }

    private static string NormalizeSemanticText(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeTagFilter(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var normalized = tag.Trim();
        return normalized.StartsWith('#') ? normalized[1..] : normalized;
    }

    private static double ComputeDiceCoefficient(string left, string right)
    {
        if (left.Length < 3 || right.Length < 3)
        {
            return left.Length > 0 && string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
        }

        var leftTrigrams = BuildTrigrams(left);
        var rightTrigrams = BuildTrigrams(right);
        if (leftTrigrams.Count == 0 || rightTrigrams.Count == 0)
        {
            return 0d;
        }

        var overlap = leftTrigrams.Count(trigram => rightTrigrams.Contains(trigram));
        return (2d * overlap) / (leftTrigrams.Count + rightTrigrams.Count);
    }

    private static HashSet<string> BuildTrigrams(string value)
    {
        var compact = Regex.Replace(value, @"\s+", " ").Trim();
        var trigrams = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index <= compact.Length - 3; index++)
        {
            trigrams.Add(compact.Substring(index, 3));
        }

        return trigrams;
    }

    [GeneratedRegex(@"[a-z0-9][a-z0-9_\-/\.]*", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex SemanticTokenRegex();

    private static readonly HashSet<string> SemanticStopWords =
    [
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "from", "in", "into", "is", "it",
        "of", "on", "or", "that", "the", "to", "with", "we", "you", "your", "our", "this", "these", "those"
    ];

    private static DateTimeOffset ToDateTimeOffset(long? unixMilliseconds)
    {
        return unixMilliseconds is > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value)
            : DateTimeOffset.UnixEpoch;
    }

    private sealed record SemanticChunk(
        string Path,
        int Index,
        int GlobalIndex,
        string Text,
        int TokenCount,
        IReadOnlyDictionary<string, int> TermFrequencies,
        IReadOnlySet<string> TitleTerms,
        IReadOnlySet<string> Tags,
        IReadOnlySet<string> FrontmatterTerms);
}
