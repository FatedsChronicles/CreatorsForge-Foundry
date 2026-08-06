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
                request.Surface.Elements.Count > MaximumElements ||
                !IsAdapterDescriptorBounded(request.Surface.Adapter))
            {
                return await WriteFailureAsync(resultPath, "Preview frame exceeds the bounded runtime contract.");
            }

            var adapterResult = PreviewProviderAdapterRegistry.Render(request.Surface);
            var frame = new PreviewRuntimeFrame(
                request.SessionId,
                request.Generation,
                DateTimeOffset.UtcNow,
                request.Surface.Kind,
                request.Surface.Source,
                request.Surface.ViewportWidth,
                request.Surface.ViewportHeight,
                request.Surface.SourceSha256,
                adapterResult.Elements,
                adapterResult.AdapterId,
                adapterResult.DisplayName);
            var logs = new List<string>
            {
                $"Accepted bounded {request.Surface.Kind} frame generation {request.Generation}.",
                $"Selected provider adapter {adapterResult.AdapterId}: {adapterResult.DisplayName}.",
                $"Rendered {adapterResult.Elements.Count} visual elements at {frame.ViewportWidth} x {frame.ViewportHeight}.",
            };
            logs.AddRange(adapterResult.Logs);
            logs.Add("Project assemblies, scripts, browser engines, and native plugins were not loaded by the Phase 22C adapters.");
            await WriteAsync(resultPath, new PreviewRuntimeHostResult(true, frame, logs, null));
            Console.WriteLine($"Preview generation {request.Generation} completed.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return await WriteFailureAsync(resultPath, exception.Message);
        }
    }

    private static bool IsAdapterDescriptorBounded(PreviewAdapterDescriptor? adapter)
    {
        if (adapter is null)
        {
            return true;
        }
        return adapter.Id.Length is > 0 and <= 64 &&
            adapter.DisplayName.Length is > 0 and <= 128 &&
            adapter.Metadata.Count <= 12 &&
            adapter.Metadata.All(item =>
                item.Key.Length is > 0 and <= 64 &&
                item.Value.Length <= 256);
    }

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
