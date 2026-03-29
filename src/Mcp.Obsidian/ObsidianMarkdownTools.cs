using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal static partial class ObsidianMarkdownTools
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public static JsonArray ExtractLinks(string markdown)
    {
        var links = new JsonArray();

        foreach (Match match in WikiLinkRegex().Matches(markdown))
        {
            var raw = match.Groups["target"].Value.Trim();
            var isEmbed = match.Value.StartsWith("![[", StringComparison.Ordinal);
            var parts = raw.Split('|', 2);
            var destination = parts[0].Trim();
            var display = parts.Length > 1 ? parts[1].Trim() : null;

            links.Add(new JsonObject
            {
                ["type"] = "wikilink",
                ["embed"] = isEmbed,
                ["target"] = destination,
                ["display"] = display,
                ["normalizedTarget"] = NormalizeInternalLinkTarget(destination),
            });
        }

        foreach (Match match in MarkdownLinkRegex().Matches(markdown))
        {
            var url = match.Groups["url"].Value.Trim();
            links.Add(new JsonObject
            {
                ["type"] = "markdown",
                ["text"] = match.Groups["text"].Value,
                ["target"] = url,
                ["isExternal"] = Uri.TryCreate(url, UriKind.Absolute, out _),
            });
        }

        return links;
    }

    public static JsonArray ParseHeadingPaths(string markdown)
    {
        var results = new JsonArray();
        var stack = new List<string>();

        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith('#'))
            {
                continue;
            }

            var level = 0;
            while (level < trimmed.Length && trimmed[level] == '#')
            {
                level++;
            }

            if (level == 0 || level >= trimmed.Length || trimmed[level] != ' ')
            {
                continue;
            }

            var headingText = trimmed[(level + 1)..].Trim();
            if (headingText.Length == 0)
            {
                continue;
            }

            while (stack.Count >= level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            stack.Add(headingText);
            results.Add(new JsonObject
            {
                ["level"] = level,
                ["heading"] = headingText,
                ["path"] = string.Join("::", stack),
            });
        }

        return results;
    }

    public static string? ResolveHeadingTarget(string markdown, IReadOnlyList<string> requestedPath)
    {
        var normalizedRequestedPath = requestedPath
            .Select(NormalizeHeadingName)
            .Where(static segment => segment.Length > 0)
            .ToArray();

        if (normalizedRequestedPath.Length == 0)
        {
            return null;
        }

        foreach (var heading in ParseHeadingEntries(markdown))
        {
            if (heading.NormalizedPath.SequenceEqual(normalizedRequestedPath, StringComparer.Ordinal))
            {
                return heading.Path;
            }
        }

        return null;
    }

    public static HeadingAppendPlan BuildHeadingAppendPlan(string markdown, IReadOnlyList<string> requestedPath)
    {
        var normalizedRequestedPath = requestedPath
            .Select(static segment => segment.Trim())
            .Where(static segment => segment.Length > 0)
            .ToArray();

        if (normalizedRequestedPath.Length == 0)
        {
            throw new InvalidOperationException("A heading path is required.");
        }

        var headings = ParseHeadingEntries(markdown);
        for (var prefixLength = normalizedRequestedPath.Length; prefixLength > 0; prefixLength--)
        {
            var prefix = normalizedRequestedPath.Take(prefixLength).Select(NormalizeHeadingName).ToArray();
            var existingHeading = headings.FirstOrDefault(heading => heading.NormalizedPath.SequenceEqual(prefix, StringComparer.Ordinal));
            if (existingHeading is null)
            {
                continue;
            }

            if (prefixLength == normalizedRequestedPath.Length)
            {
                return new HeadingAppendPlan(existingHeading.Path, null);
            }

            var missingBuilder = new List<string>();
            for (var index = prefixLength; index < normalizedRequestedPath.Length; index++)
            {
                missingBuilder.Add($"{new string('#', index + 1)} {normalizedRequestedPath[index]}");
                missingBuilder.Add(string.Empty);
            }

            return new HeadingAppendPlan(existingHeading.Path, string.Join('\n', missingBuilder).TrimEnd());
        }

        var newHeadingBuilder = new List<string>();
        for (var index = 0; index < normalizedRequestedPath.Length; index++)
        {
            newHeadingBuilder.Add($"{new string('#', index + 1)} {normalizedRequestedPath[index]}");
            newHeadingBuilder.Add(string.Empty);
        }

        return new HeadingAppendPlan(null, string.Join('\n', newHeadingBuilder).TrimEnd());
    }

    public static JsonObject BuildBacklinkReport(
        string targetPath,
        string markdown,
        JsonNode? metadata,
        JsonArray searchResults,
        IReadOnlyDictionary<string, string> candidateNoteContents)
    {
        var noteTitle = Path.GetFileNameWithoutExtension(targetPath);
        var aliases = ExtractAliases(metadata);
        var outgoingLinks = ExtractLinks(markdown);
        var targetNames = aliases.Prepend(noteTitle)
            .Select(NormalizeHeadingName)
            .Where(static name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var normalizedPathStem = NormalizeInternalLinkTarget(targetPath);

        var linkedBy = new JsonArray();
        var mentionCandidates = new JsonArray();

        foreach (var candidate in candidateNoteContents)
        {
            if (PathComparer.Equals(candidate.Key, targetPath))
            {
                continue;
            }

            var candidateLinks = ExtractLinks(candidate.Value)
                .OfType<JsonObject>()
                .Where(static item => item["type"]?.GetValue<string>() == "wikilink")
                .ToArray();

            var linksToTarget = candidateLinks
                .Where(link =>
                {
                    var normalizedTarget = link["normalizedTarget"]?.GetValue<string>() ?? string.Empty;
                    return normalizedTarget == normalizedPathStem ||
                           normalizedTarget == NormalizeInternalLinkTarget(noteTitle);
                })
                .ToArray();

            if (linksToTarget.Length > 0)
            {
                linkedBy.Add(new JsonObject
                {
                    ["path"] = candidate.Key,
                    ["links"] = new JsonArray(linksToTarget.Select(link => link.DeepClone()).ToArray()),
                });
                continue;
            }

            if (ContainsPlainMention(candidate.Value, targetNames))
            {
                mentionCandidates.Add(new JsonObject
                {
                    ["path"] = candidate.Key,
                    ["reason"] = "Text mention found without an explicit Obsidian link.",
                });
            }
        }

        return new JsonObject
        {
            ["targetPath"] = targetPath,
            ["title"] = noteTitle,
            ["aliases"] = new JsonArray(aliases.Select(static alias => (JsonNode)alias).ToArray()),
            ["outgoingLinks"] = outgoingLinks,
            ["linkedBy"] = linkedBy,
            ["suggestedMentions"] = mentionCandidates,
            ["searchResults"] = searchResults.DeepClone(),
        };
    }

    private static bool ContainsPlainMention(string content, IReadOnlyCollection<string> names)
    {
        foreach (var name in names)
        {
            if (name.Length == 0)
            {
                continue;
            }

            if (Regex.IsMatch(content, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ExtractAliases(JsonNode? metadata)
    {
        var aliasesNode = metadata?["frontmatter"]?["aliases"];
        return aliasesNode switch
        {
            JsonArray aliasesArray => aliasesArray
                .Select(static item => item?.GetValue<string>()?.Trim())
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Cast<string>()
                .ToArray(),
            JsonValue aliasValue when aliasValue.TryGetValue<string>(out var alias) && !string.IsNullOrWhiteSpace(alias) => [alias.Trim()],
            _ => [],
        };
    }

    private static IEnumerable<HeadingEntry> ParseHeadingEntries(string markdown)
    {
        return ParseHeadingPaths(markdown)
            .OfType<JsonObject>()
            .Select(static item =>
            {
                var path = item["path"]?.GetValue<string>() ?? string.Empty;
                var segments = path.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new HeadingEntry(path, segments.Select(NormalizeHeadingName).ToArray());
            });
    }

    private static string NormalizeInternalLinkTarget(string target)
    {
        var normalized = target.Trim();
        var headingIndex = normalized.IndexOf('#');
        if (headingIndex >= 0)
        {
            normalized = normalized[..headingIndex];
        }

        return NormalizeHeadingName(Path.GetFileNameWithoutExtension(normalized));
    }

    private static string NormalizeHeadingName(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"!?\[\[(?<target>[^\]]+)\]\]", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex WikiLinkRegex();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    private sealed record HeadingEntry(string Path, IReadOnlyList<string> NormalizedPath);
}

internal sealed record HeadingAppendPlan(string? ExistingTarget, string? MissingHeadingMarkdown);
