using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddHttpClientInstrumentation()
    .AddSource("*")
    .AddOtlpExporter()
    .Build();
using var metricsProvider = Sdk.CreateMeterProviderBuilder()
    .AddHttpClientInstrumentation()
    .AddMeter("*")
    .AddOtlpExporter()
    .Build();
using var loggerFactory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(opt => opt.AddOtlpExporter()));

// Connect to MCP servers
Console.WriteLine("Connecting client to MCP 'everything' server and windows-theme-toggle server");

// Create OpenAI client (or any other compatible with IChatClient)
// Provide your own OPENAI_API_KEY via an environment variable.
var openAIClient = new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")).GetChatClient("gpt-4o-mini");

// Create a sampling client.
using IChatClient samplingClient = openAIClient.AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(loggerFactory: loggerFactory, configure: o => o.EnableSensitiveData = true)
    .Build();

// "everything" MCP server
var everythingClient = await McpClient.CreateAsync(
    new StdioClientTransport(new()
    {
        Command = "npx",
        Arguments = ["-y", "--verbose", "@modelcontextprotocol/server-everything"],
        Name = "Everything",
    }),
    clientOptions: new()
    {
        Handlers = new()
        {
            SamplingHandler = samplingClient.CreateSamplingHandler()
        }
    },
    loggerFactory: loggerFactory);

// Local windows-theme-toggle MCP server (stdio)
var themeToggleClient = await McpClient.CreateAsync(
    new StdioClientTransport(new()
    {
        Command = "c:\\Users\\t-ste\\Documents\\GitHub\\2025-winter-break\\python-mcp-playing\\.venv\\Scripts\\python.exe",
        Arguments = ["c:\\Users\\t-ste\\Documents\\GitHub\\2025-winter-break\\python-mcp-playing\\src\\theme_toggle_server.py"],
        Name = "WindowsThemeToggle",
    }),
    clientOptions: new()
    {
        Handlers = new()
        {
            SamplingHandler = samplingClient.CreateSamplingHandler()
        }
    },
    loggerFactory: loggerFactory);

// Get all available tools
Console.WriteLine("Tools available (from all servers):");
var toolsEverything = await everythingClient.ListToolsAsync();
var toolsThemeToggle = await themeToggleClient.ListToolsAsync();
var tools = toolsEverything.Concat(toolsThemeToggle).ToList();
foreach (var tool in tools)
{
    Console.WriteLine($"  {tool}");
}

Console.WriteLine();

// Create an IChatClient that can use the tools.
using IChatClient chatClient = openAIClient.AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry(loggerFactory: loggerFactory, configure: o => o.EnableSensitiveData = true)
    .Build();

// Have a conversation, making all tools available to the LLM.
List<ChatMessage> messages = [];
while (true)
{
    Console.Write("Q: ");
    messages.Add(new(ChatRole.User, Console.ReadLine()));

    List<ChatResponseUpdate> updates = [];
    await foreach (var update in chatClient.GetStreamingResponseAsync(messages, new() { Tools = [.. tools] }))
    {
        Console.Write(update);
        updates.Add(update);
    }
    Console.WriteLine();

    messages.AddMessages(updates);
}