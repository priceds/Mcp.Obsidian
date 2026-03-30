using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Mcp.Obsidian;

internal sealed class McpHttpServer(ObsidianToolRegistry toolRegistry, int port)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly List<HttpResponse> _sseClients = [];
    private readonly Lock _sseLock = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Urls.Add($"http://localhost:{port}");

        app.MapGet("/sse", async (HttpContext context) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            await context.Response.Body.FlushAsync(cancellationToken);

            lock (_sseLock)
            {
                _sseClients.Add(context.Response);
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                lock (_sseLock)
                {
                    _sseClients.Remove(context.Response);
                }
            }
        });

        app.MapPost("/mcp", async ([FromBody] JsonObject message) =>
        {
            try
            {
                var responsePayload = await HandleMessageAsync(message, cancellationToken);
                return Results.Json(responsePayload, options: JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        await app.StartAsync(cancellationToken);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private async Task<JsonObject> HandleMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var method = message["method"]?.GetValue<string>();
        var id = message["id"];
        var parameters = message["params"] as JsonObject ?? [];

        JsonObject result = method switch
        {
            "initialize" => new()
            {
                ["protocolVersion"] = parameters["protocolVersion"]?.GetValue<string>() ?? "2025-03-26",
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false },
                    ["resources"] = new JsonObject { ["listChanged"] = false },
                },
                ["serverInfo"] = new JsonObject { ["name"] = "Mcp.Obsidian", ["version"] = "1.0.0" },
            },
            "tools/list" => new JsonObject { ["tools"] = toolRegistry.ListTools() },
            "tools/call" => await HandleToolCallAsync(parameters, cancellationToken),
            "resources/list" => await HandleResourcesListAsync(cancellationToken),
            "resources/read" => await HandleResourcesReadAsync(parameters, cancellationToken),
            "ping" => new JsonObject(),
            _ => new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = -32601,
                    ["message"] = $"Method '{method}' not found.",
                },
            },
        };

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        };
    }

    private async Task<JsonObject> HandleToolCallAsync(JsonObject parameters, CancellationToken cancellationToken)
    {
        var toolName = parameters["name"]?.GetValue<string>() ?? string.Empty;
        var arguments = parameters["arguments"] as JsonObject ?? [];
        var result = await toolRegistry.InvokeAsync(toolName, arguments, cancellationToken);
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = result.Text,
                },
            },
            ["isError"] = result.IsError,
            ["structuredContent"] = result.StructuredContent,
        };
    }

    private async Task<JsonObject> HandleResourcesListAsync(CancellationToken cancellationToken)
    {
        var resources = new JsonArray();
        foreach (var path in await toolRegistry.ListVaultPathsAsync(cancellationToken))
        {
            resources.Add(new JsonObject
            {
                ["uri"] = $"obsidian://vault/{path}",
                ["name"] = Path.GetFileNameWithoutExtension(path),
                ["mimeType"] = "text/markdown",
            });
        }

        return new JsonObject { ["resources"] = resources };
    }

    private async Task<JsonObject> HandleResourcesReadAsync(JsonObject parameters, CancellationToken cancellationToken)
    {
        var uri = parameters["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri))
        {
            return new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = -32602,
                    ["message"] = "Missing uri parameter.",
                },
            };
        }

        const string prefix = "obsidian://vault/";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = -32602,
                    ["message"] = "Unsupported URI scheme.",
                },
            };
        }

        var text = await toolRegistry.ReadNoteContentAsync(uri[prefix.Length..], cancellationToken);
        return new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = text,
                },
            },
        };
    }
}
