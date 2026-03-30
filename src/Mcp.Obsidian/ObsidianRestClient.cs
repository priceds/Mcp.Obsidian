using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal sealed class ObsidianRestClient : IDisposable, IObsidianClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly HttpClient _httpClient;

    public ObsidianRestClient(ObsidianSettings settings)
    {
        var handler = new HttpClientHandler();
        if (!settings.VerifySsl)
        {
            handler.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{settings.BaseUrl}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(60),
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mcp.Obsidian/1.0");
    }

    public async Task<JsonNode?> ReadResourceAsync(
        ObsidianResource resource,
        ObsidianReadFormat format,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildResourceRoute(resource));
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(format switch
        {
            ObsidianReadFormat.Markdown => "text/markdown",
            ObsidianReadFormat.NoteJson => "application/vnd.olrapi.note+json",
            ObsidianReadFormat.DocumentMap => "application/vnd.olrapi.document-map+json",
            _ => "text/markdown",
        }));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return format == ObsidianReadFormat.Markdown
            ? JsonValue.Create(await ReadTextResponseAsync(response, cancellationToken))
            : await ReadJsonResponseAsync(response, cancellationToken);
    }

    public async Task<string> WriteResourceAsync(
        ObsidianResource resource,
        string content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildResourceRoute(resource))
        {
            Content = new StringContent(content, Encoding.UTF8, "text/markdown"),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadTextResponseAsync(response, cancellationToken);
    }

    public async Task<string> AppendResourceAsync(
        ObsidianResource resource,
        string content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildResourceRoute(resource))
        {
            Content = new StringContent(content, Encoding.UTF8, "text/markdown"),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadTextResponseAsync(response, cancellationToken);
    }

    public async Task DeleteResourceAsync(ObsidianResource resource, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync(BuildResourceRoute(resource), cancellationToken);
        var body = await ReadResponseBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
    }

    public async Task<string> PatchResourceAsync(
        ObsidianResource resource,
        ObsidianPatchRequest patch,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, BuildResourceRoute(resource))
        {
            Content = new StringContent(SerializePatchContent(patch.Content, patch.ContentType), Encoding.UTF8, patch.ContentType),
        };
        request.Headers.TryAddWithoutValidation("Operation", patch.Operation);
        request.Headers.TryAddWithoutValidation("Target-Type", patch.TargetType);
        request.Headers.TryAddWithoutValidation("Target", patch.Target);
        request.Headers.TryAddWithoutValidation("Target-Delimiter", patch.Delimiter);
        request.Headers.TryAddWithoutValidation("Trim-Target-Whitespace", patch.TrimTargetWhitespace ? "true" : "false");
        request.Headers.TryAddWithoutValidation("Create-Target-If-Missing", patch.CreateTargetIfMissing ? "true" : "false");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadTextResponseAsync(response, cancellationToken);
    }

    public async Task<JsonNode?> SearchSimpleAsync(string query, int? contextLength, CancellationToken cancellationToken)
    {
        var route = $"search/simple/?query={Uri.EscapeDataString(query)}";
        if (contextLength is not null)
        {
            route += $"&contextLength={contextLength.Value}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonResponseAsync(response, cancellationToken);
    }

    public async Task<JsonNode?> QueryVaultAsync(ObsidianSearchQuery query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "search/")
        {
            Content = query.Language switch
            {
                "dataview" => new StringContent(query.TextQuery ?? string.Empty, Encoding.UTF8, "application/vnd.olrapi.dataview.dql+txt"),
                "jsonlogic" => new StringContent(
                    query.JsonLogicQuery?.ToJsonString(JsonOptions) ?? "{}",
                    Encoding.UTF8,
                    "application/vnd.olrapi.jsonlogic+json"),
                _ => throw new InvalidOperationException($"Unsupported search language '{query.Language}'."),
            },
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonResponseAsync(response, cancellationToken);
    }

    public async Task<JsonNode?> ListFilesAsync(string? folder, CancellationToken cancellationToken)
    {
        var normalizedFolder = string.IsNullOrWhiteSpace(folder) ? string.Empty : folder.Trim().Trim('/');
        var route = string.IsNullOrEmpty(normalizedFolder)
            ? "vault/"
            : $"vault/{EncodePathSegments(normalizedFolder)}/";

        using var response = await _httpClient.GetAsync(route, cancellationToken);
        return await ReadJsonResponseAsync(response, cancellationToken);
    }

    public async Task<JsonNode?> ListCommandsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("commands/", cancellationToken);
        return await ReadJsonResponseAsync(response, cancellationToken);
    }

    public async Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"commands/{Uri.EscapeDataString(commandId)}/");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await ReadResponseBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
    }

    public async Task OpenNoteAsync(string path, bool newLeaf, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"open/{EncodePathSegments(path.Trim().TrimStart('/'))}?newLeaf={(newLeaf ? "true" : "false")}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await ReadResponseBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string BuildResourceRoute(ObsidianResource resource)
    {
        return resource.Scope switch
        {
            "vault" => $"vault/{EncodePathSegments(RequirePath(resource.Path))}",
            "active" => "active/",
            "periodic" => BuildPeriodicRoute(resource),
            _ => throw new InvalidOperationException($"Unsupported resource scope '{resource.Scope}'."),
        };
    }

    private static string BuildPeriodicRoute(ObsidianResource resource)
    {
        var period = string.IsNullOrWhiteSpace(resource.Period) ? "daily" : resource.Period.Trim().ToLowerInvariant();
        var allowed = new[] { "daily", "weekly", "monthly", "quarterly", "yearly" };
        if (!allowed.Contains(period, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported periodic note period '{period}'.");
        }

        if (resource.Date is null)
        {
            return $"periodic/{period}/";
        }

        var date = resource.Date.Value;
        return $"periodic/{period}/{date.Year}/{date.Month}/{date.Day}/";
    }

    private static string RequirePath(string? path)
    {
        var trimmedPath = path?.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            throw new InvalidOperationException("A vault-relative path is required.");
        }

        return trimmedPath;
    }

    private static string SerializePatchContent(JsonNode? content, string contentType)
    {
        if (content is null)
        {
            return string.Empty;
        }

        return contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            ? content.ToJsonString(JsonOptions)
            : content.GetValue<string>();
    }

    private static string EncodePathSegments(string path)
    {
        return string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString));
    }

    private static async Task<string> ReadTextResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadResponseBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return body;
    }

    private static async Task<JsonNode?> ReadJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadResponseBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return JsonNode.Parse(body);
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new ObsidianApiException(response.StatusCode, body);
    }
}
