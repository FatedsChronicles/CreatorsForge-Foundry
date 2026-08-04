using System.Text.Json;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Core.Tests.Projects;

public sealed class FoundryProjectLoaderTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task LoadAsyncLoadsSampleAndPreservesUnknownFields()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HelloFoundry.foundryproj");

        var result = await FoundryProjectLoader.LoadAsync(
            projectPath,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Manifest);
        Assert.Equal("Hello Foundry", result.Manifest.Name);
        Assert.True(result.Manifest.AdditionalProperties?.ContainsKey("futureSetting"));
        Assert.True(result.Manifest.Target?.AdditionalProperties?.ContainsKey("releaseChannel"));

        var json = JsonSerializer.Serialize(result.Manifest, SerializerOptions);
        Assert.Contains("\"futureSetting\"", json, StringComparison.Ordinal);
        Assert.Contains("\"releaseChannel\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsyncReportsJsonLocationWithoutThrowing()
    {
        var projectPath = CreateTemporaryProject("""{"schemaVersion":"wrong"}""");

        try
        {
            var result = await FoundryProjectLoader.LoadAsync(
                projectPath,
                CancellationToken.None);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.False(result.IsSuccess);
            Assert.Equal("CFL0005", diagnostic.Code);
            Assert.Equal("$.schemaVersion", diagnostic.Location?.JsonPath);
            Assert.NotNull(diagnostic.Location?.Line);
            Assert.NotNull(diagnostic.SuggestedFix);
        }
        finally
        {
            File.Delete(projectPath);
        }
    }

    [Fact]
    public async Task LoadAsyncReportsMissingFile()
    {
        var projectPath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.foundryproj");

        var result = await FoundryProjectLoader.LoadAsync(
            projectPath,
            CancellationToken.None);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CFL0001", diagnostic.Code);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task LoadAsyncHonorsCancellation()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HelloFoundry.foundryproj");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FoundryProjectLoader.LoadAsync(projectPath, cancellation.Token));
    }

    private static string CreateTemporaryProject(string content)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.foundryproj");
        File.WriteAllText(path, content);
        return path;
    }
}
