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
            var client = new ObsidianRestClient(settings);
            var toolRegistry = new ObsidianToolRegistry(client);
            var server = new McpServer(toolRegistry, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);

            await server.RunAsync(CancellationToken.None);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }
}
