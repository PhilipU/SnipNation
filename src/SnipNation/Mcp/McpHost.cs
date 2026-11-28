using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnipNation.Imaging;

namespace SnipNation.Mcp;

internal static class McpHost
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    internal static int Run()
    {
        try
        {
            var app = new App { McpMode = true };
            app.InitializeComponent();
            var dispatcher = app.Dispatcher;
            var server = Task.Run(async () =>
            {
                try
                {
                    await RunServer(dispatcher);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                }
            });
            Dispatcher.Run();
            server.GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"SnipNation MCP: {error.Message}");
            return 1;
        }
    }

    private static async Task RunServer(Dispatcher dispatcher)
    {
        var screenshots = new ScreenshotTools(dispatcher);
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var method in typeof(ScreenshotTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.IsDefined(typeof(McpServerToolAttribute)))
                tools.Add(McpServerTool.Create(method, screenshots, new() { SerializerOptions = Json, OpenWorld = false }));
        }

        var prompts = new McpServerPrimitiveCollection<McpServerPrompt>();
        prompts.Add(McpServerPrompt.Create(CreateGuide, new()
        {
            Name = "create_guide",
            Description = "Plan a step-by-step guide with annotated SnipNation screenshots."
        }));
        var version = typeof(McpHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.0.0";
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "SnipNation", Version = version },
            ServerInstructions = "Capture and annotate screenshots in the current Windows session. " +
                "Use list_capture_targets to identify a window or display. " +
                "Use get_screenshot to inspect a screenshot and its revision before editing. " +
                "Positions for edits are image pixels measured from the top-left of the current crop. " +
                "Export each finished image for use in a guide.",
            ToolCollection = tools,
            PromptCollection = prompts
        };

        await using var server = McpServer.Create(new StdioServerTransport(options), options);
        await server.RunAsync();
    }

    private static string CreateGuide(
        [Description("What the reader should be able to do after following the guide.")] string goal,
        [Description("Absolute Windows folder for the exported images.")] string outputFolder) =>
        $"""
        Create a short, step-by-step guide for this task: {goal}
        Save the guide images in {outputFolder}.
        Identify the relevant window and have the user display each step as needed.
        Capture that window or ask the user to select the relevant region.
        Inspect the screenshot and use numbered callouts, frames, or arrows to identify the controls.
        Check the edited preview and export a PNG with a descriptive name for each step.
        Write numbered instructions with links to the exported images.
        """;

    internal static CallToolResult Result(object metadata, BitmapSource? image = null)
    {
        var structured = JsonSerializer.SerializeToElement(metadata, Json);
        var result = new CallToolResult
        {
            StructuredContent = structured,
            Content = [new TextContentBlock { Text = structured.GetRawText() }]
        };
        if (image is not null)
        {
            using var stream = new MemoryStream();
            Images.SavePng(image, stream);
            result.Content.Add(ImageContentBlock.FromBytes(stream.ToArray(), "image/png"));
        }
        return result;
    }
}
