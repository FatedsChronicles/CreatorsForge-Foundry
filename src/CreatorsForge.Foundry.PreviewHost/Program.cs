using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.PreviewHost;

internal static class Program
{
    private const int MaximumRequestBytes = 2 * 1024 * 1024;
    private const int MaximumElements = 48;
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: PreviewHost <request.json> <result.json>");
            return 2;
        }

        var resultPath = Path.GetFullPath(args[1]);
        try
        {
            var requestPath = Path.GetFullPath(args[0]);
            if (!File.Exists(requestPath) || new FileInfo(requestPath).Length > MaximumRequestBytes)
            {
                return await WriteFailureAsync(resultPath, "Preview request is missing or exceeds the 2 MiB protocol limit.");
            }

            var request = JsonSerializer.Deserialize<PreviewRuntimeRequest>(
                await File.ReadAllTextAsync(requestPath),
                ReadOptions);
            if (request?.Surface is null)
            {
                return await WriteFailureAsync(resultPath, "Preview request is invalid.");
            }
            if (request.Surface.ViewportWidth is < 240 or > 3840 ||
                request.Surface.ViewportHeight is < 180 or > 2160 ||
                request.Surface.Elements.Count > MaximumElements)
            {
                return await WriteFailureAsync(resultPath, "Preview frame exceeds the bounded runtime contract.");
            }

            var elements = request.Surface.Elements
                .Select(element => new PreviewRuntimeElement(
                    element.Kind,
                    element.Name,
                    element.Label,
                    GetVisualRole(element.Kind),
                    Math.Clamp(element.X, 0, request.Surface.ViewportWidth),
                    Math.Clamp(element.Y, 0, request.Surface.ViewportHeight),
                    Math.Clamp(element.Width, 20, request.Surface.ViewportWidth),
                    Math.Clamp(element.Height, 20, request.Surface.ViewportHeight)))
                .ToArray();
            var frame = new PreviewRuntimeFrame(
                request.SessionId,
                request.Generation,
                DateTimeOffset.UtcNow,
                request.Surface.Kind,
                request.Surface.Source,
                request.Surface.ViewportWidth,
                request.Surface.ViewportHeight,
                request.Surface.SourceSha256,
                elements);
            var logs = new[]
            {
                $"Accepted bounded {request.Surface.Kind} frame generation {request.Generation}.",
                $"Rendered {elements.Length} visual elements at {frame.ViewportWidth} x {frame.ViewportHeight}.",
                "Project assemblies and scripts were not loaded by the generic Phase 22B host.",
            };
            await WriteAsync(resultPath, new PreviewRuntimeHostResult(true, frame, logs, null));
            Console.WriteLine($"Preview generation {request.Generation} completed.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return await WriteFailureAsync(resultPath, exception.Message);
        }
    }

    private static string GetVisualRole(string kind) => kind.ToLowerInvariant() switch
    {
        "button" => "action",
        "input" or "textbox" or "richtextbox" or "combobox" or "listbox" => "input",
        "header" or "nav" or "footer" => "chrome",
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "label" => "heading",
        "img" or "picturebox" => "media",
        "obs-canvas" => "canvas",
        "obs-template" => "badge",
        _ => "panel",
    };

    private static async Task<int> WriteFailureAsync(string resultPath, string message)
    {
        await WriteAsync(resultPath, new PreviewRuntimeHostResult(false, null, [], message));
        Console.Error.WriteLine(message);
        return 1;
    }

    private static Task WriteAsync(string path, PreviewRuntimeHostResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new ArgumentException("Result path has no parent directory."));
        return File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(result, WriteOptions) + "\n",
            new UTF8Encoding(false));
    }
}
