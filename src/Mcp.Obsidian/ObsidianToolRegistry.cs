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
            Tool("obsidian_list_files", "List files under a vault folder, or the vault root when folder is omitted.", Schema(new JsonObject
            {
                ["folder"] = StringProperty("Optional vault-relative folder path."),
            })),
            Tool("obsidian_read_resource", "Read a vault note, the active note, or a periodic note as markdown, parsed note JSON, or document map.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
                ["format"] = EnumProperty("Response format.", "markdown", "note_json", "document_map"),
            }, "scope")),
            Tool("obsidian_write_resource", "Create or replace a vault note, active note, or periodic note with markdown content.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
                ["content"] = StringProperty("Markdown content to write."),
            }, "scope", "content")),
            Tool("obsidian_append_resource", "Append markdown to a vault note, active note, or periodic note.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
                ["content"] = StringProperty("Markdown content to append."),
            }, "scope", "content")),
            Tool("obsidian_delete_resource", "Delete a vault note, the active note, or a periodic note.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
            }, "scope")),
            Tool("obsidian_patch_target", "Patch content relative to a heading, block, or frontmatter field in a vault, active, or periodic note.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
                ["operation"] = EnumProperty("Patch operation.", "append", "prepend", "replace"),
                ["target_type"] = EnumProperty("Patch target type.", "heading", "block", "frontmatter"),
                ["target"] = StringProperty("Heading path, block reference, or frontmatter field."),
                ["content"] = new JsonObject
                {
                    ["description"] = "String for markdown patch content or any JSON value when content_type is application/json.",
                },
                ["content_type"] = EnumProperty("Request content type.", "text/markdown", "application/json"),
                ["delimiter"] = StringProperty("Delimiter for nested heading targets."),
                ["trim_target_whitespace"] = BoolProperty("Trim whitespace from target before patching."),
                ["create_target_if_missing"] = BoolProperty("Create a frontmatter field or patch target if it is missing."),
            }, "scope", "operation", "target_type", "target", "content")),
            Tool("obsidian_patch_frontmatter", "Update one or more frontmatter fields on a vault, active, or periodic note.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
                ["updates"] = ObjectProperty("Frontmatter fields to update."),
            }, "scope", "updates")),
            Tool("obsidian_daily_note", "Shorthand helper for reading, writing, appending, deleting, or patching the current or dated daily note.", Schema(new JsonObject
            {
                ["action"] = EnumProperty("Daily note action.", "read", "write", "append", "delete", "patch_frontmatter"),
                ["date"] = StringProperty("Optional daily note date in YYYY-MM-DD format."),
                ["format"] = EnumProperty("Read format.", "markdown", "note_json", "document_map"),
                ["content"] = StringProperty("Markdown content for write or append."),
                ["updates"] = ObjectProperty("Frontmatter fields for patch_frontmatter."),
            }, "action")),
            Tool("obsidian_search_simple", "Run text search across the vault using the plugin's simple search endpoint.", Schema(new JsonObject
            {
                ["query"] = StringProperty("Search terms."),
                ["context_length"] = IntegerProperty("Optional context length around each match."),
            }, "query")),
            Tool("obsidian_query_vault", "Run a Dataview DQL or JsonLogic query across the vault.", Schema(new JsonObject
            {
                ["language"] = EnumProperty("Query language.", "dataview", "jsonlogic"),
                ["query"] = StringProperty("Dataview DQL query string."),
                ["jsonlogic"] = ObjectProperty("JsonLogic query object."),
            }, "language")),
            Tool("obsidian_open_note", "Open a vault note in the Obsidian desktop UI and optionally create it if missing.", Schema(new JsonObject
            {
                ["path"] = StringProperty("Vault-relative note path."),
                ["new_leaf"] = BoolProperty("Open in a new leaf."),
            }, "path")),
            Tool("obsidian_list_commands", "List commands available in the running Obsidian instance.", Schema(new JsonObject())),
            Tool("obsidian_execute_command", "Execute a command in the running Obsidian instance by command id.", Schema(new JsonObject
            {
                ["command_id"] = StringProperty("Command id from obsidian_list_commands."),
            }, "command_id")),
            Tool("obsidian_extract_links", "Extract wikilinks, embeds, and markdown links from a note.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
            }, "scope")),
            Tool("obsidian_backlink_report", "Analyze outgoing links, explicit backlinks, and plain-text mentions for a vault note.", Schema(new JsonObject
            {
                ["path"] = StringProperty("Vault-relative note path."),
            }, "path")),
            Tool("obsidian_smart_append", "Append below a heading path if it exists, or create the missing heading structure first.", Schema(new JsonObject
            {
                ["scope"] = EnumProperty("Resource scope.", "vault", "active", "periodic"),
                ["path"] = StringProperty("Vault-relative path. Required when scope is vault."),
                ["period"] = EnumProperty("Periodic note period.", "daily", "weekly", "monthly", "quarterly", "yearly"),
                ["date"] = StringProperty("Optional periodic note date in YYYY-MM-DD format."),
                ["heading"] = StringProperty("Heading path like 'Projects::Q2::Notes'."),
                ["heading_path"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Alternative explicit heading path segments.",
                },
                ["content"] = StringProperty("Markdown content to append under the heading."),
                ["create_if_missing"] = BoolProperty("Create missing heading structure when necessary."),
            }, "scope", "content")),
            Tool("obsidian_scaffold_workspace", "Create a folder-based Obsidian workspace scaffold with index notes and a welcome note.", Schema(new JsonObject
            {
                ["root_folder"] = StringProperty("Vault-relative folder to scaffold, such as Work or ClientX."),
                ["folders"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Subfolders to create with index notes.",
                },
                ["create_index_notes"] = BoolProperty("Create README notes inside each folder."),
                ["index_note_name"] = StringProperty("Name for folder index notes, default README.md."),
                ["welcome_note_name"] = StringProperty("Root welcome note name, default README.md."),
                ["include_daily_notes_folder"] = BoolProperty("Add a Daily folder to the scaffold."),
            }, "root_folder")),

            Tool("obsidian_read_note", "Backward-compatible alias for reading a vault note as markdown.", Schema(new JsonObject
            {
                ["path"] = StringProperty("Vault-relative note path."),
            }, "path")),
            Tool("obsidian_create_note", "Backward-compatible alias for creating or replacing a vault note.", Schema(new JsonObject
            {
                ["path"] = StringProperty("Vault-relative note path."),
                ["content"] = StringProperty("Markdown content to write."),
            }, "path", "content")),
            Tool("obsidian_append", "Backward-compatible alias for appending to a vault note.", Schema(new JsonObject
            {
                ["path"] = StringProperty("Vault-relative note path."),
                ["content"] = StringProperty("Markdown content to append."),
            }, "path", "content")),
            Tool("obsidian_search", "Backward-compatible alias for simple vault text search.", Schema(new JsonObject
            {
                ["query"] = StringProperty("Search terms."),
                ["context_length"] = IntegerProperty("Optional context length around each match."),
            }, "query")),
        ];
    }

    public async Task<McpToolCallResult> InvokeAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        try
        {
            return name switch
            {
                "obsidian_list_files" => Success(await _client.ListFilesAsync(GetOptionalString(arguments, "folder"), cancellationToken)),
                "obsidian_read_resource" => await ReadResourceAsync(arguments, cancellationToken),
                "obsidian_write_resource" => await WriteResourceAsync(arguments, cancellationToken),
                "obsidian_append_resource" => await AppendResourceAsync(arguments, cancellationToken),
                "obsidian_delete_resource" => await DeleteResourceAsync(arguments, cancellationToken),
                "obsidian_patch_target" => await PatchTargetAsync(arguments, cancellationToken),
                "obsidian_patch_frontmatter" => await PatchFrontmatterAsync(arguments, cancellationToken),
                "obsidian_daily_note" => await DailyNoteAsync(arguments, cancellationToken),
                "obsidian_search_simple" => Success(await _client.SearchSimpleAsync(
                    GetRequiredString(arguments, "query"),
                    GetOptionalInt(arguments, "context_length"),
                    cancellationToken)),
                "obsidian_query_vault" => Success(await _client.QueryVaultAsync(ParseSearchQuery(arguments), cancellationToken)),
                "obsidian_open_note" => await OpenNoteAsync(arguments, cancellationToken),
                "obsidian_list_commands" => Success(await _client.ListCommandsAsync(cancellationToken)),
                "obsidian_execute_command" => await ExecuteCommandAsync(arguments, cancellationToken),
                "obsidian_extract_links" => await ExtractLinksAsync(arguments, cancellationToken),
                "obsidian_backlink_report" => await BuildBacklinkReportAsync(arguments, cancellationToken),
                "obsidian_smart_append" => await SmartAppendAsync(arguments, cancellationToken),
                "obsidian_scaffold_workspace" => await ScaffoldWorkspaceAsync(arguments, cancellationToken),
                "obsidian_read_note" => Success(await _client.ReadResourceAsync(
                    new ObsidianResource("vault", GetRequiredString(arguments, "path")),
                    ObsidianReadFormat.Markdown,
                    cancellationToken)),
                "obsidian_create_note" => Success(await WriteVaultNoteAsync(arguments, cancellationToken)),
                "obsidian_append" => Success(await AppendVaultNoteAsync(arguments, cancellationToken)),
                "obsidian_search" => Success(await _client.SearchSimpleAsync(
                    GetRequiredString(arguments, "query"),
                    GetOptionalInt(arguments, "context_length"),
                    cancellationToken)),
                _ => McpToolCallResult.Error($"Unknown tool '{name}'."),
            };
        }
        catch (Exception exception)
        {
            return McpToolCallResult.Error(exception.Message);
        }
    }

    private async Task<McpToolCallResult> ReadResourceAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        var format = ParseReadFormat(GetOptionalString(arguments, "format") ?? "markdown");
        var result = await _client.ReadResourceAsync(resource, format, cancellationToken);
        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["format"] = GetOptionalString(arguments, "format") ?? "markdown",
            ["result"] = result,
        });
    }

    private async Task<McpToolCallResult> WriteResourceAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        var response = await _client.WriteResourceAsync(resource, GetRequiredString(arguments, "content"), cancellationToken);
        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        });
    }

    private async Task<McpToolCallResult> AppendResourceAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        var response = await _client.AppendResourceAsync(resource, GetRequiredString(arguments, "content"), cancellationToken);
        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        });
    }

    private async Task<McpToolCallResult> DeleteResourceAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        await _client.DeleteResourceAsync(resource, cancellationToken);
        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["result"] = "deleted",
        });
    }

    private async Task<McpToolCallResult> PatchTargetAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        var patch = new ObsidianPatchRequest(
            GetRequiredString(arguments, "operation"),
            GetRequiredString(arguments, "target_type"),
            GetRequiredString(arguments, "target"),
            arguments["content"]?.DeepClone(),
            GetOptionalString(arguments, "content_type") ?? "text/markdown",
            GetOptionalString(arguments, "delimiter") ?? "::",
            GetOptionalBool(arguments, "trim_target_whitespace") ?? false,
            GetOptionalBool(arguments, "create_target_if_missing") ?? false);

        var response = await _client.PatchResourceAsync(resource, patch, cancellationToken);

        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["target"] = patch.Target,
            ["targetType"] = patch.TargetType,
            ["operation"] = patch.Operation,
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        });
    }

    private async Task<McpToolCallResult> PatchFrontmatterAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        var updates = GetRequiredObject(arguments, "updates");
        var results = new JsonArray();

        foreach (var update in updates)
        {
            if (update.Key is null || update.Value is null)
            {
                continue;
            }

            var response = await _client.PatchResourceAsync(
                resource,
                new ObsidianPatchRequest(
                    "replace",
                    "frontmatter",
                    update.Key,
                    update.Value.DeepClone(),
                    "application/json",
                    "::",
                    false,
                    true),
                cancellationToken);

            results.Add(new JsonObject
            {
                ["field"] = update.Key,
                ["result"] = string.IsNullOrWhiteSpace(response) ? "updated" : response,
            });
        }

        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["updates"] = results,
        });
    }

    private async Task<McpToolCallResult> DailyNoteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var action = GetRequiredString(arguments, "action");
        var resource = new ObsidianResource("periodic", null, "daily", ParseOptionalDate(arguments, "date"));

        return action switch
        {
            "read" => Success(new JsonObject
            {
                ["resource"] = DescribeResource(resource),
                ["format"] = GetOptionalString(arguments, "format") ?? "markdown",
                ["result"] = await _client.ReadResourceAsync(resource, ParseReadFormat(GetOptionalString(arguments, "format") ?? "markdown"), cancellationToken),
            }),
            "write" => await WriteDailyNoteAsync(resource, arguments, cancellationToken),
            "append" => await AppendDailyNoteAsync(resource, arguments, cancellationToken),
            "delete" => await DeleteDailyNoteAsync(resource, cancellationToken),
            "patch_frontmatter" => await PatchDailyFrontmatterAsync(resource, arguments, cancellationToken),
            _ => McpToolCallResult.Error($"Unsupported daily note action '{action}'."),
        };
    }

    private async Task<McpToolCallResult> OpenNoteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var path = GetRequiredString(arguments, "path");
        await _client.OpenNoteAsync(path, GetOptionalBool(arguments, "new_leaf") ?? false, cancellationToken);
        return Success(new JsonObject
        {
            ["path"] = path,
            ["result"] = "opened",
        });
    }

    private async Task<McpToolCallResult> ExecuteCommandAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var commandId = GetRequiredString(arguments, "command_id");
        await _client.ExecuteCommandAsync(commandId, cancellationToken);
        return Success(new JsonObject
        {
            ["commandId"] = commandId,
            ["result"] = "executed",
        });
    }

    private async Task<McpToolCallResult> ExtractLinksAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        var markdown = (await _client.ReadResourceAsync(resource, ObsidianReadFormat.Markdown, cancellationToken))?.GetValue<string>() ?? string.Empty;

        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["links"] = ObsidianMarkdownTools.ExtractLinks(markdown),
        });
    }

    private async Task<McpToolCallResult> BuildBacklinkReportAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var path = GetRequiredString(arguments, "path");
        var resource = new ObsidianResource("vault", path);
        var markdown = (await _client.ReadResourceAsync(resource, ObsidianReadFormat.Markdown, cancellationToken))?.GetValue<string>() ?? string.Empty;
        var metadata = await _client.ReadResourceAsync(resource, ObsidianReadFormat.NoteJson, cancellationToken);
        var searchResults = await _client.SearchSimpleAsync(Path.GetFileNameWithoutExtension(path), 80, cancellationToken) as JsonArray ?? [];
        var candidateFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in searchResults.OfType<JsonObject>())
        {
            var candidatePath = result["filename"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(candidatePath) || candidateFiles.ContainsKey(candidatePath))
            {
                continue;
            }

            var candidateMarkdown = (await _client.ReadResourceAsync(
                new ObsidianResource("vault", candidatePath),
                ObsidianReadFormat.Markdown,
                cancellationToken))?.GetValue<string>() ?? string.Empty;
            candidateFiles[candidatePath] = candidateMarkdown;
        }

        return Success(ObsidianMarkdownTools.BuildBacklinkReport(path, markdown, metadata, searchResults, candidateFiles));
    }

    private async Task<McpToolCallResult> SmartAppendAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var resource = ParseResource(arguments);
        var markdown = (await _client.ReadResourceAsync(resource, ObsidianReadFormat.Markdown, cancellationToken))?.GetValue<string>() ?? string.Empty;
        var content = GetRequiredString(arguments, "content");
        var headingPath = GetHeadingPath(arguments);
        var createIfMissing = GetOptionalBool(arguments, "create_if_missing") ?? true;
        var plan = ObsidianMarkdownTools.BuildHeadingAppendPlan(markdown, headingPath);

        if (plan.ExistingTarget is not null)
        {
            var response = await _client.PatchResourceAsync(
                resource,
                new ObsidianPatchRequest("append", "heading", plan.ExistingTarget, JsonValue.Create(content), "text/markdown", "::", false, createIfMissing),
                cancellationToken);

            return Success(new JsonObject
            {
                ["resource"] = DescribeResource(resource),
                ["headingTarget"] = plan.ExistingTarget,
                ["createdHeadings"] = false,
                ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
            });
        }

        var headingMarkdown = plan.MissingHeadingMarkdown ?? throw new InvalidOperationException("Unable to resolve heading append plan.");
        var combinedAppend = string.IsNullOrWhiteSpace(markdown)
            ? $"{headingMarkdown}\n\n{content}"
            : $"\n\n{headingMarkdown}\n\n{content}";
        var responseText = await _client.AppendResourceAsync(resource, combinedAppend, cancellationToken);

        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["headingTarget"] = string.Join("::", headingPath),
            ["createdHeadings"] = true,
            ["result"] = string.IsNullOrWhiteSpace(responseText) ? "ok" : responseText,
        });
    }

    private async Task<McpToolCallResult> ScaffoldWorkspaceAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var request = ParseWorkspaceRequest(arguments);
        var created = new JsonArray();
        var root = request.RootFolder.Trim().Trim('/');
        var welcomePath = $"{root}/{request.WelcomeNoteName}";
        var welcomeContent = $"# {Path.GetFileName(root)}\n\nCreated by Mcp.Obsidian.\n";
        await _client.WriteResourceAsync(new ObsidianResource("vault", welcomePath), welcomeContent, cancellationToken);
        created.Add(welcomePath);

        var folders = request.Folders.ToList();
        if (request.IncludeDailyNotesFolder && !folders.Contains("Daily", StringComparer.OrdinalIgnoreCase))
        {
            folders.Add("Daily");
        }

        foreach (var folder in folders)
        {
            if (!request.CreateIndexNotes)
            {
                continue;
            }

            var notePath = $"{root}/{folder.Trim().Trim('/')}/{request.IndexNoteName}";
            var noteTitle = folder.Trim().Trim('/');
            var content = $"# {noteTitle}\n\nWorkspace section created by Mcp.Obsidian.\n";
            await _client.WriteResourceAsync(new ObsidianResource("vault", notePath), content, cancellationToken);
            created.Add(notePath);
        }

        return Success(new JsonObject
        {
            ["rootFolder"] = root,
            ["createdNotes"] = created,
        });
    }

    private async Task<JsonObject> WriteVaultNoteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var path = GetRequiredString(arguments, "path");
        var response = await _client.WriteResourceAsync(new ObsidianResource("vault", path), GetRequiredString(arguments, "content"), cancellationToken);
        return new JsonObject
        {
            ["path"] = path,
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        };
    }

    private async Task<JsonObject> AppendVaultNoteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var path = GetRequiredString(arguments, "path");
        var response = await _client.AppendResourceAsync(new ObsidianResource("vault", path), GetRequiredString(arguments, "content"), cancellationToken);
        return new JsonObject
        {
            ["path"] = path,
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        };
    }

    private async Task<McpToolCallResult> DeleteDailyNoteAsync(ObsidianResource resource, CancellationToken cancellationToken)
    {
        await _client.DeleteResourceAsync(resource, cancellationToken);
        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["result"] = "deleted",
        });
    }

    private async Task<McpToolCallResult> WriteDailyNoteAsync(ObsidianResource resource, JsonObject arguments, CancellationToken cancellationToken)
    {
        var response = await _client.WriteResourceAsync(resource, GetRequiredString(arguments, "content"), cancellationToken);
        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        });
    }

    private async Task<McpToolCallResult> AppendDailyNoteAsync(ObsidianResource resource, JsonObject arguments, CancellationToken cancellationToken)
    {
        var response = await _client.AppendResourceAsync(resource, GetRequiredString(arguments, "content"), cancellationToken);
        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["result"] = string.IsNullOrWhiteSpace(response) ? "ok" : response,
        });
    }

    private async Task<McpToolCallResult> PatchDailyFrontmatterAsync(ObsidianResource resource, JsonObject arguments, CancellationToken cancellationToken)
    {
        var updates = GetRequiredObject(arguments, "updates");
        var results = new JsonArray();

        foreach (var update in updates)
        {
            if (update.Key is null || update.Value is null)
            {
                continue;
            }

            var response = await _client.PatchResourceAsync(
                resource,
                new ObsidianPatchRequest("replace", "frontmatter", update.Key, update.Value.DeepClone(), "application/json", "::", false, true),
                cancellationToken);

            results.Add(new JsonObject
            {
                ["field"] = update.Key,
                ["result"] = string.IsNullOrWhiteSpace(response) ? "updated" : response,
            });
        }

        return Success(new JsonObject
        {
            ["resource"] = DescribeResource(resource),
            ["updates"] = results,
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

    private static JsonObject Schema(JsonObject properties, params string[] required)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(static value => (JsonNode)value).ToArray()),
            ["additionalProperties"] = false,
        };
    }

    private static JsonObject StringProperty(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    private static JsonObject IntegerProperty(string description) => new()
    {
        ["type"] = "integer",
        ["description"] = description,
    };

    private static JsonObject BoolProperty(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
    };

    private static JsonObject ObjectProperty(string description) => new()
    {
        ["type"] = "object",
        ["description"] = description,
    };

    private static JsonObject EnumProperty(string description, params string[] values) => new()
    {
        ["type"] = "string",
        ["description"] = description,
        ["enum"] = new JsonArray(values.Select(static value => (JsonNode)value).ToArray()),
    };

    private static ObsidianResource ParseResource(JsonObject arguments)
    {
        var scope = GetRequiredString(arguments, "scope");
        return scope switch
        {
            "vault" => new ObsidianResource("vault", GetRequiredString(arguments, "path")),
            "active" => new ObsidianResource("active"),
            "periodic" => new ObsidianResource("periodic", null, GetOptionalString(arguments, "period") ?? "daily", ParseOptionalDate(arguments, "date")),
            _ => throw new InvalidOperationException($"Unsupported resource scope '{scope}'."),
        };
    }

    private static ObsidianReadFormat ParseReadFormat(string value)
    {
        return value switch
        {
            "markdown" => ObsidianReadFormat.Markdown,
            "note_json" => ObsidianReadFormat.NoteJson,
            "document_map" => ObsidianReadFormat.DocumentMap,
            _ => throw new InvalidOperationException($"Unsupported read format '{value}'."),
        };
    }

    private static ObsidianSearchQuery ParseSearchQuery(JsonObject arguments)
    {
        var language = GetRequiredString(arguments, "language");
        return language switch
        {
            "dataview" => new ObsidianSearchQuery(language, GetRequiredString(arguments, "query"), null),
            "jsonlogic" => new ObsidianSearchQuery(language, null, GetRequiredObject(arguments, "jsonlogic")),
            _ => throw new InvalidOperationException($"Unsupported query language '{language}'."),
        };
    }

    private static ObsidianWorkspaceScaffoldRequest ParseWorkspaceRequest(JsonObject arguments)
    {
        var defaultFolders = new List<string> { "Inbox", "Projects", "Areas", "Resources", "Archive" };
        var folders = GetOptionalArray(arguments, "folders")?
            .Select(static item => item?.GetValue<string>()?.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList() ?? defaultFolders;

        return new ObsidianWorkspaceScaffoldRequest(
            GetRequiredString(arguments, "root_folder"),
            folders,
            GetOptionalBool(arguments, "create_index_notes") ?? true,
            GetOptionalString(arguments, "index_note_name") ?? "README.md",
            GetOptionalString(arguments, "welcome_note_name") ?? "README.md",
            GetOptionalBool(arguments, "include_daily_notes_folder") ?? true);
    }

    private static JsonObject DescribeResource(ObsidianResource resource)
    {
        var json = new JsonObject
        {
            ["scope"] = resource.Scope,
        };

        if (resource.Path is not null)
        {
            json["path"] = resource.Path;
        }

        if (resource.Period is not null)
        {
            json["period"] = resource.Period;
        }

        if (resource.Date is not null)
        {
            json["date"] = resource.Date.Value.ToString("yyyy-MM-dd");
        }

        return json;
    }

    private static IReadOnlyList<string> GetHeadingPath(JsonObject arguments)
    {
        var headingPathArray = GetOptionalArray(arguments, "heading_path");
        if (headingPathArray is not null && headingPathArray.Count > 0)
        {
            return headingPathArray
                .Select(static item => item?.GetValue<string>()?.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray();
        }

        var heading = GetOptionalString(arguments, "heading");
        if (!string.IsNullOrWhiteSpace(heading))
        {
            return heading.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        throw new InvalidOperationException("Provide either 'heading' or 'heading_path'.");
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

    private static int? GetOptionalInt(JsonObject arguments, string name)
    {
        return arguments[name]?.GetValue<int>();
    }

    private static bool? GetOptionalBool(JsonObject arguments, string name)
    {
        return arguments[name]?.GetValue<bool>();
    }

    private static JsonObject GetRequiredObject(JsonObject arguments, string name)
    {
        return arguments[name] as JsonObject
               ?? throw new InvalidOperationException($"Argument '{name}' must be an object.");
    }

    private static JsonArray? GetOptionalArray(JsonObject arguments, string name)
    {
        return arguments[name] as JsonArray;
    }

    private static DateOnly? ParseOptionalDate(JsonObject arguments, string name)
    {
        var date = GetOptionalString(arguments, name);
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        if (!DateOnly.TryParse(date, out var parsedDate))
        {
            throw new InvalidOperationException($"Argument '{name}' must be a valid date in YYYY-MM-DD format.");
        }

        return parsedDate;
    }
}

internal sealed record McpToolCallResult(bool IsError, string Text, JsonNode? StructuredContent)
{
    public static McpToolCallResult Error(string message) => new(true, message, new JsonObject { ["error"] = message });
}
