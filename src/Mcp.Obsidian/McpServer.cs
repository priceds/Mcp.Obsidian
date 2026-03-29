using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal enum McpTransportMode
{
    Unknown,
    HeaderFramed,
    NewlineDelimited,
}

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
    private readonly StreamReader _reader;
    private McpTransportMode _transportMode;

    public McpServer(ObsidianToolRegistry toolRegistry, Stream input, Stream output, TextWriter log)
    {
        _toolRegistry = toolRegistry;
        _input = input;
        _output = output;
        _log = log;
        _reader = new StreamReader(input, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _transportMode = McpTransportMode.Unknown;
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
        var firstLine = await _reader.ReadLineAsync(cancellationToken);
        if (firstLine is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return await ReadMessageAsync(cancellationToken);
        }

        if (firstLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            _transportMode = McpTransportMode.HeaderFramed;
            return await ReadHeaderFramedMessageAsync(firstLine, cancellationToken);
        }

        _transportMode = McpTransportMode.NewlineDelimited;
        return JsonNode.Parse(firstLine)?.AsObject();
    }

    private async Task<JsonObject?> ReadHeaderFramedMessageAsync(string firstHeaderLine, CancellationToken cancellationToken)
    {
        var contentLength = ParseContentLength(firstHeaderLine);
        string? line;

        while ((line = await _reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = ParseContentLength(line);
            }
        }

        if (contentLength is null)
        {
            throw new InvalidOperationException("Missing Content-Length header.");
        }

        var bodyBuffer = new char[contentLength.Value];
        var totalRead = 0;
        while (totalRead < bodyBuffer.Length)
        {
            var charsRead = await _reader.ReadAsync(bodyBuffer.AsMemory(totalRead, bodyBuffer.Length - totalRead), cancellationToken);
            if (charsRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading MCP message body.");
            }

            totalRead += charsRead;
        }

        return JsonNode.Parse(new string(bodyBuffer))?.AsObject();
    }

    private static int? ParseContentLength(string line)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var value = line[(separatorIndex + 1)..].Trim();
        return int.TryParse(value, out var parsedLength)
            ? parsedLength
            : null;
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
        if (_transportMode == McpTransportMode.NewlineDelimited)
        {
            var newlineDelimitedBody = Encoding.UTF8.GetBytes($"{Encoding.UTF8.GetString(body)}\n");
            await _output.WriteAsync(newlineDelimitedBody, cancellationToken);
            await _output.FlushAsync(cancellationToken);
            return;
        }

        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _output.WriteAsync(header, cancellationToken);
        await _output.WriteAsync(body, cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }
}
