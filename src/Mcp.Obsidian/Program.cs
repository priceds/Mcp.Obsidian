using System.Text;
using Mcp.Obsidian;

namespace Mcp.Obsidian;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var settings = ObsidianSettings.Load(args);
            IObsidianClient client = string.IsNullOrWhiteSpace(settings.VaultPath)
                ? new ObsidianRestClient(settings)
                : new ObsidianFilesystemClient(settings.VaultPath);
            var toolRegistry = new ObsidianToolRegistry(client, settings);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            var tasks = new List<Task>();

            var server = new McpServer(toolRegistry, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
            tasks.Add(server.RunAsync(cts.Token));

            if (settings.HttpPort is { } port)
            {
                var httpServer = new McpHttpServer(toolRegistry, port);
                tasks.Add(httpServer.RunAsync(cts.Token));
            }

            await Task.WhenAll(tasks);
            if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }
}
