using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal sealed class ObsidianToolRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ObsidianRestClient _client;

    public ObsidianToolRegistry(ObsidianRestClient client)
    {
        _client = client;
    }

    public JsonArray ListTools()
    {
        return
        [
            Tool(
                "obsidian_search",
                "Run a simple full-text search across the vault and return matching notes.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Search terms to pass to POST /search/simple/.",
                        },
                    },
                    ["required"] = new JsonArray("query"),
                    ["additionalProperties"] = false,
                }),
            Tool(
                "obsidian_read_note",
                "Read the full markdown contents of a note by vault-relative path.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Vault-relative file path like Notes/example.md.",
                        },
                    },
                    ["required"] = new JsonArray("path"),
                    ["additionalProperties"] = false,
                }),
            Tool(
                "obsidian_create_note",
                "Create or replace a note with markdown content.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Vault-relative file path for the note.",
                        },
                        ["content"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Markdown content to write.",
                        },
                    },
                    ["required"] = new JsonArray("path", "content"),
                    ["additionalProperties"] = false,
                }),
            Tool(
                "obsidian_append",
                "Append text to the end of an existing note.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Vault-relative path of the note to update.",
                        },
                        ["content"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Text or markdown to append.",
                        },
                    },
                    ["required"] = new JsonArray("path", "content"),
                    ["additionalProperties"] = false,
                }),
            Tool(
                "obsidian_patch_frontmatter",
                "Update one or more YAML frontmatter fields on an existing note.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Vault-relative path of the note.",
                        },
                        ["updates"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["description"] = "Object whose keys are frontmatter fields and values are the desired replacements.",
                        },
                    },
                    ["required"] = new JsonArray("path", "updates"),
                    ["additionalProperties"] = false,
                }),
            Tool(
                "obsidian_list_files",
                "List files under a vault folder. Omit folder to list the vault root.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["folder"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional vault-relative folder path.",
                        },
                    },
                    ["additionalProperties"] = false,
                }),
        ];
    }

    public async Task<McpToolCallResult> InvokeAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        try
        {
            return name switch
            {
                "obsidian_search" => Success(await _client.SearchSimpleAsync(GetRequiredString(arguments, "query"), cancellationToken)),
                "obsidian_read_note" => Success(
                    new JsonObject
                    {
                        ["path"] = GetRequiredString(arguments, "path"),
                        ["content"] = await _client.ReadNoteAsync(GetRequiredString(arguments, "path"), cancellationToken),
                    }),
                "obsidian_create_note" => await CreateNoteAsync(arguments, cancellationToken),
                "obsidian_append" => Success(
                    new JsonObject
                    {
                        ["path"] = GetRequiredString(arguments, "path"),
                        ["content"] = await _client.AppendToNoteAsync(
                            GetRequiredString(arguments, "path"),
                            GetRequiredString(arguments, "content"),
                            cancellationToken),
                    }),
                "obsidian_patch_frontmatter" => Success(
                    new JsonObject
                    {
                        ["path"] = GetRequiredString(arguments, "path"),
                        ["updates"] = await _client.PatchFrontmatterAsync(
                            GetRequiredString(arguments, "path"),
                            GetRequiredObject(arguments, "updates"),
                            cancellationToken),
                    }),
                "obsidian_list_files" => Success(await _client.ListFilesAsync(GetOptionalString(arguments, "folder"), cancellationToken)),
                _ => McpToolCallResult.Error($"Unknown tool '{name}'."),
            };
        }
        catch (Exception exception)
        {
            return McpToolCallResult.Error(exception.Message);
        }
    }

    private async Task<McpToolCallResult> CreateNoteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var path = GetRequiredString(arguments, "path");
        var content = GetRequiredString(arguments, "content");
        var response = await _client.CreateOrReplaceNoteAsync(path, content, cancellationToken);

        return Success(new JsonObject
        {
            ["path"] = path,
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        });
    }

    private static McpToolCallResult Success(JsonNode? payload)
    {
        var text = payload?.ToJsonString(JsonOptions) ?? "{}";
        return new McpToolCallResult(false, text, payload);
    }

    private static JsonObject Tool(string name, string description, JsonObject inputSchema)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema,
        };
    }

    private static string GetRequiredString(JsonObject arguments, string name)
    {
        var value = GetOptionalString(arguments, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required argument '{name}'.");
        }

        return value;
    }

    private static string? GetOptionalString(JsonObject arguments, string name)
    {
        return arguments[name]?.GetValue<string>();
    }

    private static JsonObject GetRequiredObject(JsonObject arguments, string name)
    {
        return arguments[name] as JsonObject
               ?? throw new InvalidOperationException($"Argument '{name}' must be an object.");
    }
}

internal sealed record McpToolCallResult(bool IsError, string Text, JsonNode? StructuredContent)
{
    public static McpToolCallResult Error(string message) => new(true, message, new JsonObject { ["error"] = message });
}
