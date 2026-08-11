using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Cli;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.Cli.Tests;

public sealed class FoundryCliTests
{
    [Fact]
    public async Task ValidateReturnsSuccessForSampleProject()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HelloFoundry.foundryproj");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await FoundryCli.RunAsync(
            ["validate", projectPath],
            output,
            error,
            cancellationToken: CancellationToken.None);

        Assert.Equal(FoundryCli.SuccessExitCode, exitCode);
        Assert.Equal(
            $"Project is valid: Hello Foundry (com.creatorsforge.samples.hello) 0.1.0{Environment.NewLine}",
            output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task ValidateReturnsDiagnosticExitCodeForInvalidProject()
    {
        var projectPath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.foundryproj");
        await File.WriteAllTextAsync(
            projectPath,
            """
            {
              "schemaVersion": 1,
              "name": "",
              "id": "Not Valid",
              "version": "1",
              "target": null,
              "outputs": []
            }
            """,
            CancellationToken.None);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await FoundryCli.RunAsync(
                ["validate", projectPath],
                output,
                error,
                cancellationToken: CancellationToken.None);

            Assert.Equal(FoundryCli.DiagnosticErrorExitCode, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("error CFP0002", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("$.target.provider", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("Fix:", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(projectPath);
        }
    }

    [Fact]
    public async Task InvalidCommandReturnsUsageExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await FoundryCli.RunAsync(
            [],
            output,
            error,
            cancellationToken: CancellationToken.None);

        Assert.Equal(FoundryCli.UsageErrorExitCode, exitCode);
        Assert.Empty(output.ToString());
        Assert.Equal(
            $"Usage: foundry validate <project.foundryproj>{Environment.NewLine}" +
            $"       foundry build <project.foundryproj>{Environment.NewLine}" +
            $"       foundry release <project.foundryproj>{Environment.NewLine}" +
            $"       foundry publish validate <project.foundryproj>{Environment.NewLine}" +
            $"       foundry publish <project.foundryproj>{Environment.NewLine}" +
            $"       foundry version <project.foundryproj> <major|minor|patch|version>{Environment.NewLine}" +
            $"       foundry test <project.foundryproj> [--obs <installation>]{Environment.NewLine}" +
            $"       foundry test-matrix <project.foundryproj> [--obs <installation> ...]{Environment.NewLine}" +
            $"       foundry validate-workspace <workspace.foundryworkspace>{Environment.NewLine}" +
            $"       foundry build-workspace <workspace.foundryworkspace>{Environment.NewLine}" +
            $"       foundry migrate <project.foundryproj> [--apply]{Environment.NewLine}" +
            $"       foundry template export <project.foundryproj> <output.foundrytemplate>{Environment.NewLine}" +
            $"       foundry template import <template.foundrytemplate> <directory> --name <name> --id <id> --profile <profile>{Environment.NewLine}" +
            $"       foundry sdk status obsstudio [--cache <directory>]{Environment.NewLine}" +
            $"       foundry sdk install obsstudio [--cache <directory>] [--archives <directory>]{Environment.NewLine}",
            error.ToString());
    }

    [Fact]
    public async Task ValidateWorkspaceReportsMissingWorkspace()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var missing = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".foundryworkspace");

        var exitCode = await FoundryCli.RunAsync(
            ["validate-workspace", missing],
            output,
            error,
            cancellationToken: CancellationToken.None);

        Assert.Equal(FoundryCli.DiagnosticErrorExitCode, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("CFW1302", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigratePreviewsThenAppliesSchemaZeroProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "FoundryCliMigration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "Legacy.foundryproj");
        await File.WriteAllTextAsync(projectPath, """
            {
              "schemaVersion": 0,
              "name": "Legacy CLI",
              "id": "com.example.legacy-cli",
              "version": "0.1.0",
              "target": { "provider": "streamerbot", "profile": "1.0.4-stable" },
              "features": { "mockRuntime": true },
              "managedBuild": {
                "targetFramework": "net481",
                "languageVersion": "7.3",
                "assemblyName": "Legacy.Cli",
                "sources": ["src/EntryPoint.cs"]
              },
              "cphInlineBridge": { "contract": "args-log-v1", "entryType": "Legacy.Cli.EntryPoint", "entryMethod": "Execute" },
              "targetDefinition": "streamerbot/streamerbot.json",
              "outputs": ["managedLibrary", "cphInlineBridge", "streamerBotPackage"]
            }
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            var preview = await FoundryCli.RunAsync(["migrate", projectPath], output, error);
            Assert.Equal(FoundryCli.SuccessExitCode, preview);
            Assert.Contains("Preview only", output.ToString(), StringComparison.Ordinal);

            output.GetStringBuilder().Clear();
            var applied = await FoundryCli.RunAsync(["migrate", projectPath, "--apply"], output, error);
            Assert.Equal(FoundryCli.SuccessExitCode, applied);
            Assert.Contains("Migration succeeded", output.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(projectPath + ".schema0.backup"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SdkStatusReportsMissingExplicitCache()
    {
        var cache = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await FoundryCli.RunAsync(
            ["sdk", "status", "obsstudio", "--cache", cache],
            output,
            error);

        Assert.Equal(FoundryCli.DiagnosticErrorExitCode, exitCode);
        Assert.Contains("OBS SDK 32.1.2 not ready", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task BuildReturnsSuccessAndReportsDeterministicOutputPaths()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HelloFoundry.foundryproj");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await FoundryCli.RunAsync(
            ["build", projectPath],
            output,
            error,
            new SuccessfulBuildRunner(),
            CancellationToken.None);

        Assert.Equal(FoundryCli.SuccessExitCode, exitCode);
        Assert.Equal(
            $"Build succeeded: Hello Foundry 0.1.0{Environment.NewLine}" +
            "Managed assembly: build/managed/CreatorsForge.Samples.HelloFoundry.dll" +
            Environment.NewLine +
            $"CPHInline bridge: build/bridge/CPHInline.cs{Environment.NewLine}" +
            "Streamer.bot package: build/streamerbot/com.creatorsforge.samples.hello.streamerbot" +
            Environment.NewLine +
            "Streamer.bot package report: build/streamerbot/package-report.json" +
            Environment.NewLine +
            "Streamer.bot portability report: build/streamerbot/portability-report.json" +
            Environment.NewLine +
            $"Package IR: build/package-ir.json{Environment.NewLine}",
            output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task ReleaseRunsValidatedBuildAndReportsVerifiedBundle()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HelloFoundry.foundryproj");
        var releaseRoot = Path.Combine(
            Path.GetDirectoryName(projectPath)!,
            "build",
            "release");
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var exitCode = await FoundryCli.RunAsync(
                ["release", projectPath],
                output,
                error,
                new SuccessfulBuildRunner(),
                CancellationToken.None);

            Assert.Equal(FoundryCli.SuccessExitCode, exitCode);
            Assert.Equal(
                $"Release succeeded: Hello Foundry 0.1.0{Environment.NewLine}" +
                $"Release bundle: build/release/com.creatorsforge.samples.hello-0.1.0{Environment.NewLine}" +
                $"Release archive: build/release/com.creatorsforge.samples.hello-0.1.0-foundry.zip{Environment.NewLine}" +
                $"Build manifest: build/release/com.creatorsforge.samples.hello-0.1.0/foundry-build.json{Environment.NewLine}" +
                $"Reproducibility report: build/release/com.creatorsforge.samples.hello-0.1.0-reproducibility.json{Environment.NewLine}",
                output.ToString());
            Assert.Empty(error.ToString());
            Assert.True(File.Exists(Path.Combine(
                releaseRoot,
                "com.creatorsforge.samples.hello-0.1.0-foundry.zip")));
        }
        finally
        {
            if (Directory.Exists(releaseRoot))
            {
                Directory.Delete(releaseRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TestBuildsAndRunsStructuredMockCases()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HelloFoundry.foundryproj");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await FoundryCli.RunAsync(
            ["test", projectPath],
            output,
            error,
            new SuccessfulBuildRunner(),
            CancellationToken.None);

        Assert.Equal(FoundryCli.SuccessExitCode, exitCode);
        Assert.Contains("[PASSED] Logs the message supplied by a command event", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("[PASSED] Uses the defensive default", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Tests passed: 2 passed, 0 failed, 0 errors.", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task TestMatrixRunsEveryDeclaredStreamerBotProfile()
    {
        var sourceRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "CreatorsForge.Foundry.Cli.Matrix", Guid.NewGuid().ToString("N"));
        CopyFixtureFile(sourceRoot, temporaryRoot, "HelloFoundry.foundryproj");
        CopyFixtureFile(sourceRoot, temporaryRoot, Path.Combine("src", "HelloExtension.cs"));
        CopyFixtureFile(sourceRoot, temporaryRoot, Path.Combine("streamerbot", "streamerbot.json"));
        CopyFixtureFile(sourceRoot, temporaryRoot, Path.Combine("tests", "foundry-tests.json"));
        var projectPath = Path.Combine(temporaryRoot, "HelloFoundry.foundryproj");
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var exitCode = await FoundryCli.RunAsync(
                ["test-matrix", projectPath],
                output,
                error,
                new SuccessfulBuildRunner(),
                CancellationToken.None);

            Assert.Equal(FoundryCli.SuccessExitCode, exitCode);
            Assert.Contains("[PASSED] 1.0.4-stable (mock-runtime-v1)", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("[PASSED] 1.0.5-alpha.34 (mock-runtime-v1)", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("[PASSED] 1.0.5-beta.1 (mock-runtime-v1)", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("[PASSED] 1.0.5-beta.6 (mock-runtime-v1)", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("[PASSED] 1.0.7-stable (mock-runtime-v1)", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Matrix passed: 5 passed, 0 failed, 0 errors.", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task VersionAndPublishCommandsCreateStrictDistributionEvidence()
    {
        var sourceRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "CreatorsForge.Foundry.Cli.Publish", Guid.NewGuid().ToString("N"));
        CopyFixtureFile(sourceRoot, temporaryRoot, "HelloFoundry.foundryproj");
        CopyFixtureFile(sourceRoot, temporaryRoot, Path.Combine("src", "HelloExtension.cs"));
        CopyFixtureFile(sourceRoot, temporaryRoot, Path.Combine("streamerbot", "streamerbot.json"));
        CopyFixtureFile(sourceRoot, temporaryRoot, Path.Combine("tests", "foundry-tests.json"));
        var projectPath = Path.Combine(temporaryRoot, "HelloFoundry.foundryproj");
        try
        {
            var workspace = await FoundryWorkspaceService.OpenAsync(projectPath);
            var saved = await FoundryPublishingService.SaveMetadataAsync(workspace.Value!, new FoundryPublishing
            {
                PackageName = "com.creatorsforge.samples.hello",
                Summary = "CLI publishing fixture.",
                Authors = ["Creators Forge"],
            });
            Assert.True(saved.IsSuccess);
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "LICENSE.txt"), "MIT\n");
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "CHANGELOG.md"), "# 0.1.1\n\nPublishing test.\n");

            using var versionOutput = new StringWriter();
            using var versionError = new StringWriter();
            var versionExit = await FoundryCli.RunAsync(["version", projectPath, "patch"], versionOutput, versionError);
            Assert.Equal(FoundryCli.SuccessExitCode, versionExit);
            Assert.Equal($"Version updated: 0.1.1{Environment.NewLine}", versionOutput.ToString());

            using var output = new StringWriter();
            using var error = new StringWriter();
            var exitCode = await FoundryCli.RunAsync(
                ["publish", projectPath], output, error, new SuccessfulBuildRunner());
            Assert.Equal(FoundryCli.SuccessExitCode, exitCode);
            Assert.Contains("Publish succeeded: Hello Foundry 0.1.1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Reproducibility report:", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
            Assert.True(File.Exists(Path.Combine(temporaryRoot, "build", "release", "com.creatorsforge.samples.hello-0.1.1-foundry.zip")));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void CopyFixtureFile(string sourceRoot, string destinationRoot, string relativePath)
    {
        var destination = Path.Combine(destinationRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(sourceRoot, relativePath), destination);
    }

    private sealed class SuccessfulBuildRunner : IBuildProcessRunner
    {
        public async Task<BuildProcessResult> RunAsync(
            BuildProcessRequest request,
            CancellationToken cancellationToken)
        {
            var outputIndex = request.Arguments
                .Select((value, index) => (value, index))
                .Single(item => item.value == "--output")
                .index;
            var outputDirectory = request.Arguments[outputIndex + 1];
            Directory.CreateDirectory(outputDirectory);
            File.Copy(
                typeof(CreatorsForge.Samples.HelloFoundry.HelloExtension).Assembly.Location,
                Path.Combine(outputDirectory, "CreatorsForge.Samples.HelloFoundry.dll"),
                overwrite: true);
            return new(0, "Build succeeded.", string.Empty);
        }
    }
}
