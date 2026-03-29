using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mcp.Obsidian;

internal sealed class ObsidianRestClient : IDisposable
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

    public async Task<string> ReadNoteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(BuildVaultPath(path), cancellationToken);
        return await ReadTextResponseAsync(response, cancellationToken);
    }

    public async Task<string> CreateOrReplaceNoteAsync(string path, string content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildVaultPath(path))
        {
            Content = new StringContent(content, Encoding.UTF8, "text/markdown"),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadTextResponseAsync(response, cancellationToken);
    }

    public async Task<JsonNode?> SearchSimpleAsync(string query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"search/simple/?query={Uri.EscapeDataString(query)}");
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

    public async Task<string> AppendToNoteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var existingContent = await ReadNoteAsync(path, cancellationToken);
        var needsSeparator = existingContent.Length > 0 &&
                             !existingContent.EndsWith('\n') &&
                             !content.StartsWith('\n');
        var newContent = needsSeparator
            ? $"{existingContent}\n{content}"
            : $"{existingContent}{content}";

        await CreateOrReplaceNoteAsync(path, newContent, cancellationToken);
        return newContent;
    }

    public async Task<JsonArray> PatchFrontmatterAsync(
        string path,
        JsonObject updates,
        CancellationToken cancellationToken)
    {
        var results = new JsonArray();

        foreach (var update in updates)
        {
            if (update.Key is null || update.Value is null)
            {
                continue;
            }

            using var request = new HttpRequestMessage(HttpMethod.Patch, BuildVaultPath(path))
            {
                Content = new StringContent(
                    update.Value.ToJsonString(JsonOptions),
                    Encoding.UTF8,
                    "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Operation", "replace");
            request.Headers.TryAddWithoutValidation("Target-Type", "frontmatter");
            request.Headers.TryAddWithoutValidation("Target", update.Key);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await ReadResponseBodyAsync(response, cancellationToken);
            EnsureSuccess(response, body);

            results.Add(new JsonObject
            {
                ["field"] = update.Key,
                ["result"] = string.IsNullOrWhiteSpace(body) ? "updated" : body,
            });
        }

        return results;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string BuildVaultPath(string path)
    {
        var trimmedPath = path.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            throw new InvalidOperationException("A note path is required.");
        }

        return $"vault/{EncodePathSegments(trimmedPath)}";
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

internal sealed class ObsidianApiException(HttpStatusCode statusCode, string responseBody)
    : Exception($"Obsidian API request failed with {(int)statusCode} {statusCode}. {responseBody}".Trim())
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string ResponseBody { get; } = responseBody;
}
