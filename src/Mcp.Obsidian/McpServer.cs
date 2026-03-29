using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal sealed class McpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly ObsidianToolRegistry _toolRegistry;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly TextWriter _log;

    public McpServer(ObsidianToolRegistry toolRegistry, Stream input, Stream output, TextWriter log)
    {
        _toolRegistry = toolRegistry;
        _input = input;
        _output = output;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await ReadMessageAsync(cancellationToken);
            if (message is null)
            {
                break;
            }

            try
            {
                await HandleMessageAsync(message, cancellationToken);
            }
            catch (Exception exception)
            {
                _log.WriteLine(exception);
            }
        }
    }

    private async Task HandleMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var method = message["method"]?.GetValue<string>();
        var id = message["id"];
        var parameters = message["params"] as JsonObject ?? [];

        switch (method)
        {
            case "initialize":
                await WriteResultAsync(id, new JsonObject
                {
                    ["protocolVersion"] = parameters["protocolVersion"]?.GetValue<string>() ?? "2025-03-26",
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject
                        {
                            ["listChanged"] = false,
                        },
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "Mcp.Obsidian",
                        ["version"] = "1.0.0",
                    },
                    ["instructions"] = "Use the Obsidian tools to work across vault, active, and periodic notes; query with search and Dataview or JsonLogic; patch headings or frontmatter; inspect links and backlinks; open notes, run commands, and scaffold new workspaces.",
                }, cancellationToken);
                break;
            case "notifications/initialized":
                break;
            case "ping":
                await WriteResultAsync(id, new JsonObject(), cancellationToken);
                break;
            case "tools/list":
                await WriteResultAsync(id, new JsonObject
                {
                    ["tools"] = _toolRegistry.ListTools(),
                }, cancellationToken);
                break;
            case "tools/call":
                var toolName = parameters["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    await WriteErrorAsync(id, -32602, "Missing tool name.", cancellationToken);
                    return;
                }

                var arguments = parameters["arguments"] as JsonObject ?? [];
                var result = await _toolRegistry.InvokeAsync(toolName, arguments, cancellationToken);

                await WriteResultAsync(id, new JsonObject
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
                }, cancellationToken);
                break;
            default:
                if (id is not null)
                {
                    await WriteErrorAsync(id, -32601, $"Method '{method}' was not found.", cancellationToken);
                }
                break;
        }
    }

    private async Task<JsonObject?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var contentLength = await ReadContentLengthAsync(cancellationToken);
        if (contentLength is null)
        {
            return null;
        }

        var bodyBuffer = new byte[contentLength.Value];
        var totalRead = 0;
        while (totalRead < bodyBuffer.Length)
        {
            var bytesRead = await _input.ReadAsync(bodyBuffer.AsMemory(totalRead, bodyBuffer.Length - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading MCP message body.");
            }

            totalRead += bytesRead;
        }

        return JsonNode.Parse(bodyBuffer)?.AsObject();
    }

    private async Task<int?> ReadContentLengthAsync(CancellationToken cancellationToken)
    {
        string? line;
        int? contentLength = null;

        while ((line = await ReadHeaderLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                return contentLength;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = line[..separatorIndex];
            var value = line[(separatorIndex + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value, out var parsedLength))
            {
                contentLength = parsedLength;
            }
        }

        return null;
    }

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();

        while (true)
        {
            var buffer = new byte[1];
            var bytesRead = await _input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            }

            if (buffer[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            }

            bytes.Add(buffer[0]);
        }
    }

    private Task WriteResultAsync(JsonNode? id, JsonObject result, CancellationToken cancellationToken)
    {
        return WriteMessageAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        }, cancellationToken);
    }

    private Task WriteErrorAsync(JsonNode? id, int code, string message, CancellationToken cancellationToken)
    {
        return WriteMessageAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        }, cancellationToken);
    }

    private async Task WriteMessageAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        await _output.WriteAsync(header, cancellationToken);
        await _output.WriteAsync(body, cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }
}
