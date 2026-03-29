using System.Text.Json.Nodes;

namespace Mcp.Obsidian.Tests;

public sealed class MarkdownToolsTests
{
    [Fact]
    public void ExtractLinks_ReturnsWikiAndMarkdownLinks()
    {
        const string markdown = """
        Links: [[Projects/Launch Plan|launch]], ![[assets/hero.png]], [docs](https://example.com/docs)
        """;

        var links = ObsidianMarkdownTools.ExtractLinks(markdown).OfType<JsonObject>().ToArray();

        Assert.Equal(3, links.Length);
        Assert.Equal("wikilink", links[0]["type"]?.GetValue<string>());
        Assert.Equal("Projects/Launch Plan", links[0]["target"]?.GetValue<string>());
        Assert.Equal("launch", links[0]["display"]?.GetValue<string>());
        Assert.False(links[0]["embed"]?.GetValue<bool>());

        Assert.Equal("wikilink", links[1]["type"]?.GetValue<string>());
        Assert.True(links[1]["embed"]?.GetValue<bool>());
        Assert.Equal("assets/hero.png", links[1]["target"]?.GetValue<string>());

        Assert.Equal("markdown", links[2]["type"]?.GetValue<string>());
        Assert.Equal("https://example.com/docs", links[2]["target"]?.GetValue<string>());
        Assert.True(links[2]["isExternal"]?.GetValue<bool>());
    }

    [Fact]
    public void ParseHeadingPaths_BuildsNestedHeadingPaths()
    {
        const string markdown = """
        # Projects

        ## Launch

        ### Decisions
        """;

        var headings = ObsidianMarkdownTools.ParseHeadingPaths(markdown).OfType<JsonObject>().ToArray();

        Assert.Collection(
            headings,
            heading => Assert.Equal("Projects", heading["path"]?.GetValue<string>()),
            heading => Assert.Equal("Projects::Launch", heading["path"]?.GetValue<string>()),
            heading => Assert.Equal("Projects::Launch::Decisions", heading["path"]?.GetValue<string>()));
    }

    [Fact]
    public void BuildHeadingAppendPlan_UsesExactHeadingWhenPresent()
    {
        const string markdown = """
        # Projects

        ## Launch

        ### Decisions
        """;

        var plan = ObsidianMarkdownTools.BuildHeadingAppendPlan(markdown, ["Projects", "Launch", "Decisions"]);

        Assert.Equal("Projects::Launch::Decisions", plan.ExistingTarget);
        Assert.Null(plan.MissingHeadingMarkdown);
    }

    [Fact]
    public void BuildHeadingAppendPlan_CreatesMissingNestedHeadingsFromExistingPrefix()
    {
        const string markdown = """
        # Projects

        ## Launch
        """;

        var plan = ObsidianMarkdownTools.BuildHeadingAppendPlan(markdown, ["Projects", "Launch", "Decisions"]);

        Assert.Equal("Projects::Launch", plan.ExistingTarget);
        Assert.Equal("### Decisions", plan.MissingHeadingMarkdown);
    }

    [Fact]
    public void BuildHeadingAppendPlan_CreatesEntireHeadingTreeWhenNothingMatches()
    {
        const string markdown = "# Inbox";

        var plan = ObsidianMarkdownTools.BuildHeadingAppendPlan(markdown, ["Projects", "Launch"]);

        Assert.Null(plan.ExistingTarget);
        Assert.Equal(
            """
            # Projects

            ## Launch
            """,
            plan.MissingHeadingMarkdown);
    }

    [Fact]
    public void BuildBacklinkReport_FindsExplicitBacklinksAndPlainMentions()
    {
        const string targetPath = "Projects/HackerNews Launch.md";
        const string targetMarkdown = """
        # HackerNews Launch

        See also [[Marketing Plan]].
        """;

        var metadata = JsonNode.Parse(
            """
            {
              "frontmatter": {
                "aliases": ["HN Launch"]
              }
            }
            """);

        var searchResults = JsonNode.Parse(
            """
            [
              { "filename": "Notes/Weekly.md" },
              { "filename": "Notes/Todo.md" }
            ]
            """)!.AsArray();

        var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Notes/Weekly.md"] = "We should link [[HackerNews Launch]] before posting.",
            ["Notes/Todo.md"] = "Remember to prepare HN Launch assets.",
        };

        var report = ObsidianMarkdownTools.BuildBacklinkReport(targetPath, targetMarkdown, metadata, searchResults, candidates);

        var linkedBy = report["linkedBy"]!.AsArray();
        var mentions = report["suggestedMentions"]!.AsArray();
        var aliases = report["aliases"]!.AsArray();

        Assert.Single(linkedBy);
        Assert.Equal("Notes/Weekly.md", linkedBy[0]!["path"]?.GetValue<string>());
        Assert.Single(mentions);
        Assert.Equal("Notes/Todo.md", mentions[0]!["path"]?.GetValue<string>());
        Assert.Contains(aliases, alias => alias?.GetValue<string>() == "HN Launch");
    }
}
