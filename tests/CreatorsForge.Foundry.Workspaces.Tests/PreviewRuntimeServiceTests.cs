using CreatorsForge.Foundry.PreviewHost;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class PreviewRuntimeServiceTests
{
    [Fact]
    public async Task IsolatedHostProducesStyledFrameAndLifecycleStates()
    {
        using var stateRoot = new TemporaryDirectory();
        await using var session = new PreviewRuntimeSession(
            typeof(PreviewHostMarker).Assembly.Location,
            stateRoot.Path);
        var states = new List<PreviewRuntimeStatus>();
        session.StateChanged += (_, state) => states.Add(state.Status);

        var result = await session.RefreshAsync(CreateProviderSurface(
            "static-web",
            PreviewAdapterIds.StaticWeb,
            "Static web - safe document"));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(PreviewRuntimeStatus.Completed, result.Status);
        Assert.Contains(PreviewRuntimeStatus.Starting, states);
        Assert.Contains(PreviewRuntimeStatus.Running, states);
        Assert.Contains(PreviewRuntimeStatus.Completed, states);
        Assert.Equal(PreviewAdapterIds.StaticWeb, result.Frame!.AdapterId);
        Assert.Contains(result.Frame.Elements, item => item.VisualRole == "browser-chrome");
        Assert.Contains(result.Frame.Elements, item => item.Kind == "button" && item.VisualRole == "action");
        Assert.Contains(result.Logs, item => item.Contains("were not loaded", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(stateRoot.Path, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RestartCreatesANewRuntimeGeneration()
    {
        using var stateRoot = new TemporaryDirectory();
        await using var session = new PreviewRuntimeSession(
            typeof(PreviewHostMarker).Assembly.Location,
            stateRoot.Path);
        var first = await session.RefreshAsync(CreateSurface());

        var second = await session.RestartAsync(CreateSurface());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Frame!.Generation + 1, second.Frame!.Generation);
        Assert.Equal(first.Frame.SessionId, second.Frame.SessionId);
    }

    [Fact]
    public async Task MissingHostReturnsStableDiagnostic()
    {
        using var stateRoot = new TemporaryDirectory();
        await using var session = new PreviewRuntimeSession(
            System.IO.Path.Combine(stateRoot.Path, "missing-host.dll"),
            stateRoot.Path);

        var result = await session.RefreshAsync(CreateSurface());

        Assert.Equal(PreviewRuntimeStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFW2310");
    }

    [Fact]
    public async Task HostTimeoutIsContainedAndReported()
    {
        using var stateRoot = new TemporaryDirectory();
        await using var session = new PreviewRuntimeSession(
            typeof(PreviewHostMarker).Assembly.Location,
            stateRoot.Path,
            TimeSpan.Zero);

        var result = await session.RefreshAsync(CreateSurface());

        Assert.Equal(PreviewRuntimeStatus.TimedOut, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFW2312");
    }

    [Theory]
    [InlineData("static-web", PreviewAdapterIds.StaticWeb, "browser-chrome")]
    [InlineData("winforms", PreviewAdapterIds.WinForms, "form-chrome")]
    [InlineData("obs-component", PreviewAdapterIds.ObsComponent, "obs-preview")]
    public async Task ProviderAdaptersRenderDistinctBoundedFrames(
        string kind,
        string adapterId,
        string expectedRole)
    {
        using var stateRoot = new TemporaryDirectory();
        await using var session = new PreviewRuntimeSession(
            typeof(PreviewHostMarker).Assembly.Location,
            stateRoot.Path);

        var result = await session.RefreshAsync(CreateProviderSurface(
            kind,
            adapterId,
            adapterId));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(adapterId, result.Frame!.AdapterId);
        Assert.Contains(result.Frame.Elements, item => item.VisualRole == expectedRole);
        Assert.InRange(result.Frame.Elements.Count, 1, 48);
        Assert.Contains(result.Logs, item => item.Contains($"provider adapter {adapterId}", StringComparison.Ordinal));
    }

    private static PreviewDesignSurface CreateSurface() => new(
        "static-web",
        "ui/index.html",
        1280,
        720,
        64,
        new string('a', 64),
        [new("button", "start", "Start", 40, 60, 180, 48)],
        "Test structural frame.");

    private static PreviewDesignSurface CreateProviderSurface(
        string kind,
        string adapterId,
        string displayName) => new(
            kind,
            "ui/index.html",
            1280,
            720,
            64,
            new string('b', 64),
            [new("button", "start", "Start", 40, 60, 180, 48)],
            "Test provider frame.",
            new(
                adapterId,
                displayName,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["documentTitle"] = "Creator Dashboard",
                    ["windowTitle"] = "Stream Controls",
                    ["componentName"] = "Foundry Filter",
                }));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FoundryPreviewRuntimeTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
