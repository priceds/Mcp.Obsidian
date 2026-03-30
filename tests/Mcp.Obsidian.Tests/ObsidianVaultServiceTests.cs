using System.Text.Json.Nodes;

namespace Mcp.Obsidian.Tests;

public sealed class ObsidianVaultServiceTests
{
    [Fact]
    public async Task ListAllTags_CombinesFrontmatterAndInlineTags()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Projects/", "Inbox.md"],
                ["Projects"] = ["Launch.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Inbox.md"] = new(
                    """
                    ---
                    tags:
                      - ops
                    ---
                    Track #release items.
                    ```md
                    #ignored
                    ```
                    """,
                    JsonNode.Parse("""{"tags":["ops"]}""")),
                ["Projects/Launch.md"] = new(
                    """
                    ---
                    tags: [release]
                    ---
                    Launch checklist for #release and #hn-launch.
                    """,
                    JsonNode.Parse("""{"tags":["release"]}""")),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var tags = await service.ListAllTagsAsync(CancellationToken.None);

        Assert.Collection(
            tags,
            tag => Assert.Equal(("release", 2), (tag.Tag, tag.Count)),
            tag => Assert.Equal(("hn-launch", 1), (tag.Tag, tag.Count)),
            tag => Assert.Equal(("ops", 1), (tag.Tag, tag.Count)));
    }

    [Fact]
    public async Task MoveNote_RewritesMatchingWikiLinks()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Projects/", "Notes/"],
                ["Projects"] = ["Old.md", "Reference.md"],
                ["Notes"] = ["Daily.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Projects/Old.md"] = new("# Old\n"),
                ["Projects/Reference.md"] = new("See [[Old]] and [[Projects/Old#Heading|alias]]."),
                ["Notes/Daily.md"] = new("Do not touch [[Different]]."),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var result = await service.MoveNoteAsync("Projects/Old.md", "Projects/New.md", updateLinks: true, CancellationToken.None);

        Assert.Equal("Projects/Old.md", result.From);
        Assert.Equal("Projects/New.md", result.To);
        Assert.True(result.UpdatedLinks);
        Assert.Equal(1, result.UpdatedNoteCount);
        Assert.False(client.Notes.ContainsKey("Projects/Old.md"));
        Assert.True(client.Notes.ContainsKey("Projects/New.md"));
        Assert.Contains("[[New]]", client.Notes["Projects/Reference.md"].Content);
        Assert.Contains("[[New#Heading|alias]]", client.Notes["Projects/Reference.md"].Content);
    }

    [Fact]
    public async Task GetVaultStats_ComputesCountsSizesOrphansAndRecent()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Projects/", "Inbox.md"],
                ["Projects"] = ["Launch.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Inbox.md"] = new("Link to [[Launch]].", null, 15, older),
                ["Projects/Launch.md"] = new("# Launch", JsonNode.Parse("""{"tags":["ship"]}"""), 7, newer),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var stats = await service.GetVaultStatsAsync(5, CancellationToken.None);

        Assert.Equal(2, stats.NoteCount);
        Assert.Equal(1, stats.FolderCount);
        Assert.Equal(22, stats.TotalSizeBytes);
        Assert.Equal(1, stats.TagCount);
        Assert.Equal(1, stats.OrphanCount);
        Assert.Equal("Projects/Launch.md", stats.RecentlyModified[0].Path);
    }

    [Fact]
    public async Task BatchRead_ReturnsRequestedContentAndFrontmatter()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Inbox.md", "Projects/"],
                ["Projects"] = ["Launch.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Inbox.md"] = new("Inbox content", JsonNode.Parse("""{"status":"open"}"""), 13),
                ["Projects/Launch.md"] = new("Launch content", JsonNode.Parse("""{"status":"done"}"""), 14),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var notes = await service.BatchReadAsync(["Projects/Launch.md", "Inbox.md"], includeContent: true, includeFrontmatter: true, CancellationToken.None);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Inbox.md", notes[0].Path);
        Assert.Equal("open", notes[0].Frontmatter?["status"]?.GetValue<string>());
        Assert.Equal("Launch content", notes[1].Content);
    }

    [Fact]
    public async Task ExtractTasks_ParsesCompletionDueDatesPrioritiesAndTags()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Tasks.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Tasks.md"] = new(
                    """
                    - [ ] Ship release 📅 2026-04-01 ⏫ #release
                    - [x] Write notes 🔽 #docs
                    ```md
                    - [ ] ignored
                    ```
                    """),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var openTasks = await service.ExtractTasksAsync(null, completed: false, CancellationToken.None);
        var doneTasks = await service.ExtractTasksAsync(null, completed: true, CancellationToken.None);

        Assert.Single(openTasks);
        Assert.Equal("Ship release 📅 2026-04-01 ⏫ #release", openTasks[0].Text);
        Assert.Equal(new DateOnly(2026, 4, 1), openTasks[0].DueDate);
        Assert.Equal("high", openTasks[0].Priority);
        Assert.Contains("release", openTasks[0].Tags);
        Assert.Single(doneTasks);
        Assert.True(doneTasks[0].Completed);
    }

    [Fact]
    public async Task ListBrokenLinks_FindsUnresolvedWikiLinks()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Inbox.md", "Projects/"],
                ["Projects"] = ["Launch.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Inbox.md"] = new("See [[Launch]] and [[Missing Note]]."),
                ["Projects/Launch.md"] = new("See [[Another Missing#Heading]]."),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var broken = await service.ListBrokenLinksAsync(CancellationToken.None);

        Assert.Equal(2, broken.Count);
        Assert.Contains(broken, item => item.BrokenTarget == "Missing Note");
        Assert.Contains(broken, item => item.BrokenTarget == "Another Missing#Heading");
    }

    [Fact]
    public async Task ReadCanvas_ParsesNodesAndEdges()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Board.canvas"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Board.canvas"] = new(
                    """
                    {
                      "nodes": [
                        { "id": "n1", "type": "text", "text": "Hello" }
                      ],
                      "edges": [
                        { "id": "e1", "fromNode": "n1", "toNode": "n2" }
                      ]
                    }
                    """),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var canvas = await service.ReadCanvasAsync("Board.canvas", CancellationToken.None);

        Assert.Single(canvas.Nodes);
        Assert.Single(canvas.Edges);
        Assert.Equal("n1", canvas.Nodes[0].Id);
        Assert.Equal("e1", canvas.Edges[0].Id);
    }

    [Fact]
    public async Task VaultHealth_ComposesStatsBrokenLinksDuplicatesAndLargeFiles()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Inbox.md", "Projects/", "Archive/"],
                ["Projects"] = ["Launch.md", "Launch copy.md"],
                ["Archive"] = ["Launch.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Inbox.md"] = new("See [[Launch]] and [[Missing]]. #ops", JsonNode.Parse("""{"tags":["ops"]}"""), 12),
                ["Projects/Launch.md"] = new("# Launch", null, 200),
                ["Projects/Launch copy.md"] = new("# Launch copy", null, 180),
                ["Archive/Launch.md"] = new("# Launch archived", null, 160),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var health = await service.GetVaultHealthAsync(CancellationToken.None);

        Assert.NotEmpty(health.BrokenLinks);
        Assert.Contains(health.BrokenLinks, item => item.BrokenTarget == "Missing");
        Assert.Contains(health.DuplicateTitles, item => item.Title == "Launch");
        Assert.Contains("Projects/Launch.md", health.LargeFiles.Select(static item => item.Path));
        Assert.Contains(health.Tags, item => item.Tag == "ops");
    }

    [Fact]
    public async Task SearchSemantic_RanksBestMatchingNoteFirst()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Projects/", "Notes/"],
                ["Projects"] = ["HackerNews Launch.md", "Product Retro.md"],
                ["Notes"] = ["Weekly.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Projects/HackerNews Launch.md"] = new(
                    """
                    # HackerNews Launch

                    We are preparing the Hacker News launch checklist, launch assets, headline ideas, and posting strategy.
                    """,
                    JsonNode.Parse("""{"tags":["launch","hn"]}""")),
                ["Projects/Product Retro.md"] = new(
                    """
                    # Product Retro

                    Review the sprint retro notes and capture follow-up actions for the roadmap.
                    """,
                    JsonNode.Parse("""{"tags":["retro"]}""")),
                ["Notes/Weekly.md"] = new(
                    """
                    Team sync covered launch timing, but mostly staffing and status updates.
                    """),
            });

        var service = new ObsidianVaultService(client, CreateSettings());

        var results = await service.SearchSemanticAsync("hacker news launch strategy", 5, 0.10f, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("Projects/HackerNews Launch.md", results[0].Path);
        Assert.True(results[0].Score >= results[^1].Score);
        Assert.Contains("launch", results[0].MatchedTerms);
    }

    [Fact]
    public async Task BulkFrontmatter_UpdatesOnlyMatchingNotes()
    {
        var client = CreateClient(
            new Dictionary<string, string[]>
            {
                ["/"] = ["Projects/", "Notes/"],
                ["Projects"] = ["Launch.md", "Retro.md"],
                ["Notes"] = ["Inbox.md"],
            },
            new Dictionary<string, FakeNote>
            {
                ["Projects/Launch.md"] = new("Launch details #release", JsonNode.Parse("""{"status":"draft","tags":["release"]}""")),
                ["Projects/Retro.md"] = new("Retro details #retro", JsonNode.Parse("""{"status":"draft","tags":["retro"]}""")),
                ["Notes/Inbox.md"] = new("Inbox #release", JsonNode.Parse("""{"status":"draft"}""")),
            });

        var service = new ObsidianVaultService(client, CreateSettings());
        var updates = JsonNode.Parse("""{"status":"published","owner":"ops"}""")!.AsObject();

        var result = await service.BulkFrontmatterAsync("Projects", "release", updates, CancellationToken.None);

        Assert.Equal(1, result.MatchedNoteCount);
        Assert.Equal(1, result.UpdatedNoteCount);
        Assert.Single(result.Notes);
        Assert.Equal("published", client.Notes["Projects/Launch.md"].Frontmatter?["status"]?.GetValue<string>());
        Assert.Equal("ops", client.Notes["Projects/Launch.md"].Frontmatter?["owner"]?.GetValue<string>());
        Assert.Equal("draft", client.Notes["Projects/Retro.md"].Frontmatter?["status"]?.GetValue<string>());
        Assert.Null(client.Notes["Notes/Inbox.md"].Frontmatter?["owner"]);
    }

    private static FakeObsidianClient CreateClient(
        Dictionary<string, string[]> folders,
        Dictionary<string, FakeNote> notes)
    {
        return new FakeObsidianClient(folders, notes);
    }

    private static ObsidianSettings CreateSettings()
    {
        return new ObsidianSettings
        {
            BaseUrl = "https://127.0.0.1:27124",
            ApiKey = "test",
            VerifySsl = false,
            SemanticSearch = new SemanticSearchSettings(),
        };
    }

    private sealed class FakeObsidianClient : IObsidianClient
    {
        private readonly Dictionary<string, string[]> _folders;

        public FakeObsidianClient(Dictionary<string, string[]> folders, Dictionary<string, FakeNote> notes)
        {
            _folders = folders;
            Notes = new Dictionary<string, FakeNote>(notes, StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, FakeNote> Notes { get; }

        public Task<JsonNode?> ReadResourceAsync(ObsidianResource resource, ObsidianReadFormat format, CancellationToken cancellationToken)
        {
            if (!Notes.TryGetValue(resource.Path ?? string.Empty, out var note))
            {
                throw new InvalidOperationException($"Missing fake note '{resource.Path}'.");
            }

            return Task.FromResult<JsonNode?>(format switch
            {
                ObsidianReadFormat.Markdown => JsonValue.Create(note.Content),
                ObsidianReadFormat.NoteJson => new JsonObject
                {
                    ["path"] = resource.Path,
                    ["content"] = note.Content,
                    ["frontmatter"] = note.Frontmatter?.DeepClone() ?? new JsonObject(),
                    ["tags"] = new JsonArray(),
                    ["stat"] = new JsonObject
                    {
                        ["size"] = note.Size,
                        ["mtime"] = note.ModifiedAt.ToUnixTimeMilliseconds(),
                        ["ctime"] = note.ModifiedAt.ToUnixTimeMilliseconds(),
                    },
                },
                _ => JsonValue.Create(note.Content),
            });
        }

        public Task<string> WriteResourceAsync(ObsidianResource resource, string content, CancellationToken cancellationToken)
        {
            var frontmatter = Notes.TryGetValue(resource.Path ?? string.Empty, out var existing)
                ? existing.Frontmatter?.DeepClone()
                : null;
            Notes[resource.Path!] = new FakeNote(content, frontmatter, content.Length, DateTimeOffset.UtcNow);
            return Task.FromResult(string.Empty);
        }

        public Task<string> AppendResourceAsync(ObsidianResource resource, string content, CancellationToken cancellationToken)
        {
            var current = Notes[resource.Path!];
            var updated = current.Content + content;
            Notes[resource.Path!] = current with { Content = updated, Size = updated.Length };
            return Task.FromResult(string.Empty);
        }

        public Task DeleteResourceAsync(ObsidianResource resource, CancellationToken cancellationToken)
        {
            Notes.Remove(resource.Path!);
            return Task.CompletedTask;
        }

        public Task<string> PatchResourceAsync(ObsidianResource resource, ObsidianPatchRequest patch, CancellationToken cancellationToken)
        {
            if (!Notes.TryGetValue(resource.Path ?? string.Empty, out var note))
            {
                throw new InvalidOperationException($"Missing fake note '{resource.Path}'.");
            }

            if (!string.Equals(patch.TargetType, "frontmatter", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException();
            }

            var frontmatter = note.Frontmatter?.DeepClone() as JsonObject ?? new JsonObject();
            frontmatter[patch.Target] = patch.Content?.DeepClone();
            Notes[resource.Path!] = note with { Frontmatter = frontmatter };
            return Task.FromResult(string.Empty);
        }

        public Task<JsonNode?> SearchSimpleAsync(string query, int? contextLength, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<JsonNode?> QueryVaultAsync(ObsidianSearchQuery query, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<JsonNode?> ListFilesAsync(string? folder, CancellationToken cancellationToken)
        {
            var key = string.IsNullOrWhiteSpace(folder) ? "/" : folder.Trim().Trim('/');
            var items = _folders.TryGetValue(key, out var listing) ? listing : [];
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["files"] = new JsonArray(items.Select(static item => (JsonNode)item).ToArray()),
            });
        }

        public Task<JsonNode?> ListCommandsAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task OpenNoteAsync(string path, bool newLeaf, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed record FakeNote(
        string Content,
        JsonNode? Frontmatter = null,
        long Size = -1,
        DateTimeOffset? ModifiedAtValue = null)
    {
        public long Size { get; init; } = Size >= 0 ? Size : Content.Length;

        public DateTimeOffset ModifiedAt { get; init; } = ModifiedAtValue ?? DateTimeOffset.UtcNow;
    }
}
