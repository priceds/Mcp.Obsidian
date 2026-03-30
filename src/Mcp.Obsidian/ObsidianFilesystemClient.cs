using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

/// <summary>Implements IObsidianClient using direct filesystem access. No Obsidian process required.</summary>
internal sealed partial class ObsidianFilesystemClient : IObsidianClient
{
    private readonly string _root;

    public ObsidianFilesystemClient(string vaultPath)
    {
        _root = Path.GetFullPath(vaultPath);
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException($"Vault path not found: {_root}");
        }
    }

    private string Abs(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path traversal detected.");
        }

        return full;
    }

    public Task<JsonNode?> ReadResourceAsync(ObsidianResource resource, ObsidianReadFormat format, CancellationToken cancellationToken)
    {
        if (resource.Scope != "vault" || resource.Path is null)
        {
            throw new NotSupportedException("Filesystem mode only supports scope=vault with an explicit path.");
        }

        var abs = Abs(resource.Path);
        var content = File.ReadAllText(abs);
        return Task.FromResult<JsonNode?>(format switch
        {
            ObsidianReadFormat.NoteJson => BuildNoteJson(resource.Path, content, abs),
            _ => JsonValue.Create(content),
        });
    }

    public Task<string> WriteResourceAsync(ObsidianResource resource, string content, CancellationToken cancellationToken)
    {
        if (resource.Scope != "vault" || resource.Path is null)
        {
            throw new NotSupportedException("Filesystem mode only supports scope=vault.");
        }

        var abs = Abs(resource.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
        return Task.FromResult(resource.Path);
    }

    public Task<string> AppendResourceAsync(ObsidianResource resource, string content, CancellationToken cancellationToken)
    {
        if (resource.Scope != "vault" || resource.Path is null)
        {
            throw new NotSupportedException("Filesystem mode only supports scope=vault.");
        }

        var abs = Abs(resource.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.AppendAllText(abs, content);
        return Task.FromResult(resource.Path);
    }

    public Task DeleteResourceAsync(ObsidianResource resource, CancellationToken cancellationToken)
    {
        if (resource.Scope != "vault" || resource.Path is null)
        {
            throw new NotSupportedException("Filesystem mode only supports scope=vault.");
        }

        File.Delete(Abs(resource.Path));
        return Task.CompletedTask;
    }

    public Task<string> PatchResourceAsync(ObsidianResource resource, ObsidianPatchRequest patch, CancellationToken cancellationToken)
        => throw new NotSupportedException("obsidian_patch_target requires the Obsidian REST API. Use REST mode.");

    public Task<JsonNode?> SearchSimpleAsync(string query, int? contextLength, CancellationToken cancellationToken)
    {
        var results = new JsonArray();
        foreach (var file in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            if (!content.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(new JsonObject
            {
                ["filename"] = Path.GetRelativePath(_root, file).Replace('\\', '/'),
                ["score"] = 1.0,
            });
        }

        return Task.FromResult<JsonNode?>(results);
    }

    public Task<JsonNode?> QueryVaultAsync(ObsidianSearchQuery query, CancellationToken cancellationToken)
        => throw new NotSupportedException("Dataview/JsonLogic queries require the Obsidian REST API.");

    public Task<JsonNode?> ListFilesAsync(string? folder, CancellationToken cancellationToken)
    {
        var dir = folder is not null ? Abs(folder) : _root;
        if (!Directory.Exists(dir))
        {
            return Task.FromResult<JsonNode?>(new JsonArray());
        }

        var entries = new JsonArray();
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            var rel = Path.GetRelativePath(_root, entry).Replace('\\', '/');
            entries.Add(new JsonObject
            {
                ["path"] = rel,
                ["type"] = Directory.Exists(entry) ? "folder" : "file",
            });
        }

        return Task.FromResult<JsonNode?>(entries);
    }

    public Task<JsonNode?> ListCommandsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException("obsidian_list_commands requires the Obsidian REST API.");

    public Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken)
        => throw new NotSupportedException("obsidian_execute_command requires the Obsidian REST API.");

    public Task OpenNoteAsync(string path, bool newLeaf, CancellationToken cancellationToken)
        => throw new NotSupportedException("obsidian_open_note requires the Obsidian REST API.");

    private static JsonObject BuildNoteJson(string path, string content, string absolutePath)
    {
        var fileInfo = new FileInfo(absolutePath);
        return new JsonObject
        {
            ["path"] = path,
            ["content"] = content,
            ["frontmatter"] = ParseFrontmatter(content),
            ["tags"] = new JsonArray(ObsidianMarkdownTools.ExtractInlineTags(content).Select(static tag => (JsonNode)JsonValue.Create(tag)!).ToArray()),
            ["stat"] = new JsonObject
            {
                ["size"] = fileInfo.Length,
                ["mtime"] = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                ["ctime"] = new DateTimeOffset(fileInfo.CreationTimeUtc).ToUnixTimeMilliseconds(),
            },
        };
    }

    private static JsonNode? ParseFrontmatter(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return null;
        }

        var endIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return null;
        }

        var lines = normalized[4..endIndex].Split('\n');
        var result = new JsonObject();
        string? currentArrayKey = null;
        JsonArray? currentArray = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) && currentArrayKey is not null && currentArray is not null)
            {
                currentArray.Add(trimmed[2..].Trim());
                continue;
            }

            currentArrayKey = null;
            currentArray = null;

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length == 0)
            {
                currentArrayKey = key;
                currentArray = new JsonArray();
                result[key] = currentArray;
                continue;
            }

            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
            {
                var items = value[1..^1]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(static item => (JsonNode)JsonValue.Create(item.Trim('\"', '\''))!)
                    .ToArray();
                result[key] = new JsonArray(items);
                continue;
            }

            if (bool.TryParse(value, out var boolValue))
            {
                result[key] = boolValue;
                continue;
            }

            if (long.TryParse(value, out var longValue))
            {
                result[key] = longValue;
                continue;
            }

            result[key] = value.Trim('\"', '\'');
        }

        return result;
    }
}
