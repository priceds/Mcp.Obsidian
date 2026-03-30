using System.Text.Json;

namespace Mcp.Obsidian;

internal sealed class ObsidianSettings
{
    public required string BaseUrl { get; init; }

    public required string ApiKey { get; init; }

    public bool VerifySsl { get; init; } = false;

    public string? VaultPath { get; init; }

    public int? HttpPort { get; init; }

    public SemanticSearchSettings SemanticSearch { get; init; } = new();

    public static ObsidianSettings Load(string[] args)
    {
        var jsonPath = GetConfigPath(args);
        JsonElement root = default;

        if (jsonPath is not null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            root = document.RootElement.Clone();
        }

        var obsidianSection = TryGetProperty(root, "Obsidian");
        var baseUrl = GetSetting(
            environmentVariable: "OBSIDIAN__BASEURL",
            section: obsidianSection,
            propertyName: "BaseUrl",
            fallback: "https://127.0.0.1:27124");
        var apiKey = GetSetting(
            environmentVariable: "OBSIDIAN__APIKEY",
            section: obsidianSection,
            propertyName: "ApiKey",
            fallback: null);
        var vaultPath = args.FirstOrDefault(static argument => argument.StartsWith("--vault-path=", StringComparison.OrdinalIgnoreCase))
            ?["--vault-path=".Length..]
            ?? Environment.GetEnvironmentVariable("OBSIDIAN__VAULTPATH");
        var httpPortArg = args.FirstOrDefault(static argument => argument.StartsWith("--http-port=", StringComparison.OrdinalIgnoreCase))
            ?["--http-port=".Length..];
        var httpPort = httpPortArg is not null && int.TryParse(httpPortArg, out var parsedHttpPort)
            ? parsedHttpPort
            : int.TryParse(Environment.GetEnvironmentVariable("MCP_HTTP_PORT"), out var environmentHttpPort)
                ? environmentHttpPort
                : (int?)null;

        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(vaultPath))
        {
            throw new InvalidOperationException(
                "Missing Obsidian API key. Set OBSIDIAN__APIKEY or provide Obsidian:ApiKey in appsettings.json, or pass --vault-path for filesystem mode.");
        }

        var verifySsl = GetBoolSetting(
            environmentVariable: "OBSIDIAN__VERIFYSSL",
            section: obsidianSection,
            propertyName: "VerifySsl",
            fallback: false);
        var semanticSection = TryGetProperty(root, "SemanticSearch");

        return new ObsidianSettings
        {
            BaseUrl = baseUrl.TrimEnd('/'),
            ApiKey = apiKey ?? string.Empty,
            VerifySsl = verifySsl,
            VaultPath = string.IsNullOrWhiteSpace(vaultPath) ? null : Path.GetFullPath(vaultPath),
            HttpPort = httpPort,
            SemanticSearch = new SemanticSearchSettings
            {
                ModelDirectory = GetOptionalSetting("SEMANTICSEARCH__MODELDIRECTORY", semanticSection, "ModelDirectory"),
                MaxSequenceLength = GetIntSetting("SEMANTICSEARCH__MAXSEQUENCELENGTH", semanticSection, "MaxSequenceLength", 256),
            },
        };
    }

    private static string? GetConfigPath(string[] args)
    {
        var argumentPath = args.FirstOrDefault(static argument => argument.StartsWith("--config=", StringComparison.OrdinalIgnoreCase));
        if (argumentPath is not null)
        {
            return Path.GetFullPath(argumentPath["--config=".Length..]);
        }

        var environmentPath = Environment.GetEnvironmentVariable("MCP_OBSIDIAN_CONFIG");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return Path.GetFullPath(environmentPath);
        }

        var currentDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var executableDirectory = AppContext.BaseDirectory;
        var executableDirectoryPath = Path.Combine(executableDirectory, "appsettings.json");
        if (File.Exists(executableDirectoryPath))
        {
            return executableDirectoryPath;
        }

        return null;
    }

    private static JsonElement? TryGetProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value))
        {
            return value;
        }

        return null;
    }

    private static string GetSetting(
        string environmentVariable,
        JsonElement? section,
        string propertyName,
        string? fallback)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        if (section is { ValueKind: JsonValueKind.Object } jsonSection &&
            jsonSection.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(propertyValue.GetString()))
        {
            return propertyValue.GetString()!;
        }

        if (fallback is not null)
        {
            return fallback;
        }

        throw new InvalidOperationException($"Missing required configuration value for Obsidian:{propertyName}.");
    }

    private static string? GetOptionalSetting(
        string environmentVariable,
        JsonElement? section,
        string propertyName)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        if (section is { ValueKind: JsonValueKind.Object } jsonSection &&
            jsonSection.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(propertyValue.GetString()))
        {
            return propertyValue.GetString();
        }

        return null;
    }

    private static bool GetBoolSetting(
        string environmentVariable,
        JsonElement? section,
        string propertyName,
        bool fallback)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue) && bool.TryParse(environmentValue, out var parsedEnvironmentValue))
        {
            return parsedEnvironmentValue;
        }

        if (section is { ValueKind: JsonValueKind.Object } jsonSection &&
            jsonSection.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return propertyValue.GetBoolean();
        }

        return fallback;
    }

    private static int GetIntSetting(
        string environmentVariable,
        JsonElement? section,
        string propertyName,
        int fallback)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue) && int.TryParse(environmentValue, out var parsedEnvironmentValue))
        {
            return parsedEnvironmentValue;
        }

        if (section is { ValueKind: JsonValueKind.Object } jsonSection &&
            jsonSection.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.Number &&
            propertyValue.TryGetInt32(out var parsedPropertyValue))
        {
            return parsedPropertyValue;
        }

        return fallback;
    }
}

internal sealed class SemanticSearchSettings
{
    public string? ModelDirectory { get; init; }

    public int MaxSequenceLength { get; init; } = 256;
}
