using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoshopMcpServer.Services;
using PhotoshopMcpServer.Tools;

// Test Harness — invokes MCP tools directly (no MCP protocol needed)
// Usage: dotnet run -- <tool> [args...]
//
//   dotnet run --project tests/PhotoshopMcpServer.TestHarness -- debug_layer 日
//   dotnet run --project tests/PhotoshopMcpServer.TestHarness -- debug_layer --index 9
//   dotnet run --project tests/PhotoshopMcpServer.TestHarness -- get_layers font_size,font_name,text_color
//   dotnet run --project tests/PhotoshopMcpServer.TestHarness -- get_layer_info 日

var services = new ServiceCollection()
    .AddSingleton<PhotoshopBridge>()
    .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning))
    .BuildServiceProvider();

var ps = services.GetRequiredService<PhotoshopBridge>();
var logger = services.GetRequiredService<ILogger<LayerTools>>();
var layerTools = new LayerTools(ps, logger);

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run --project tests/PhotoshopMcpServer.TestHarness -- <tool> [args]");
    Console.WriteLine("  debug_layer <name|--index N>");
    Console.WriteLine("  get_layers [fields]");
    Console.WriteLine("  get_layer_info <name|--index N>");
    return 1;
}

var tool = args[0].ToLowerInvariant();
object? result = null;

try
{
    switch (tool)
    {
        case "debug_layer":
        case "debug":
            result = await CallDebugLayer(layerTools, args[1..]);
            break;
        case "get_layers":
        case "layers":
            var fields = args.Length > 1 ? args[1] : "";
            result = await layerTools.GetLayers(fields);
            break;
        case "get_layer_info":
        case "info":
            result = await CallGetLayerInfo(layerTools, args[1..]);
            break;
        default:
            Console.WriteLine($"Unknown tool: {tool}");
            return 1;
    }

    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex}");
    return 1;
}

static async Task<object> CallDebugLayer(LayerTools tools, string[] args)
{
    string name = "";
    int index = -1;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--index" && i + 1 < args.Length)
            index = int.Parse(args[++i]);
        else if (!args[i].StartsWith("--"))
            name = args[i];
    }
    return await tools.DebugLayer(name, index);
}

static async Task<object> CallGetLayerInfo(LayerTools tools, string[] args)
{
    string name = "";
    int index = -1;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--index" && i + 1 < args.Length)
            index = int.Parse(args[++i]);
        else if (!args[i].StartsWith("--"))
            name = args[i];
    }
    return await tools.GetLayerInfo(name, index);
}
