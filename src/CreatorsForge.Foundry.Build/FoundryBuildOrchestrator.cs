using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Build.ObsStudio;

namespace CreatorsForge.Foundry.Build;

public sealed class FoundryBuildOrchestrator
{
    private const int MaximumBuildDetailsCharacters = 32 * 1024;

    private static readonly JsonSerializerOptions PackageSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly Regex CompilerDiagnosticPattern = new(
        @"^(?<file>.+)\((?<line>\d+),(?<column>\d+)\): (?<severity>error|warning) (?<code>[A-Za-z]+\d+): (?<message>.*?)(?: \[[^\]]+\])?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly IBuildProcessRunner processRunner;

    public string? VisualStudioInstallationRoot { get; set; }

    public string? CMakeExecutablePath { get; set; }

    public FoundryBuildOrchestrator(IBuildProcessRunner? processRunner = null)
    {
        this.processRunner = processRunner ?? new DotNetBuildProcessRunner();
    }

    public async Task<FoundryBuildResult> BuildAsync(
        FoundryProjectManifest manifest,
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var fullProjectPath = Path.GetFullPath(projectPath);
        var projectRoot = Path.GetDirectoryName(fullProjectPath) ??
            throw new ArgumentException(
                "The project manifest path has no parent directory.",
                nameof(projectPath));
        var diagnostics = new List<FoundryDiagnostic>(
            FoundryProjectValidator.Validate(manifest, fullProjectPath));

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new(null, null, null, diagnostics);
        }

        if (manifest.Outputs.Contains(FoundryOutputKinds.ObsPlugin, StringComparer.Ordinal) ||
            manifest.Outputs.Contains(FoundryOutputKinds.ObsPluginPackage, StringComparer.Ordinal))
        {
            return await new ObsPluginBuildPipeline(
                processRunner,
                VisualStudioInstallationRoot,
                CMakeExecutablePath).BuildAsync(
                manifest,
                fullProjectPath,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
        }

        var unsupportedOutputs = manifest.Outputs
            .Where(output =>
                output is not FoundryOutputKinds.ManagedLibrary and
                    not FoundryOutputKinds.CphInlineBridge and
                    not FoundryOutputKinds.StreamerBotPackage)
            .ToArray();
        if (unsupportedOutputs.Length > 0)
        {
            diagnostics.Add(Error(
                "CFB0001",
                $"This build increment cannot produce the requested output(s): {string.Join(", ", unsupportedOutputs)}.",
                fullProjectPath,
                "$.outputs",
                "Request managedLibrary and optionally cphInlineBridge until the package adapter is implemented."));
            return new(null, null, null, diagnostics);
        }

        var requestsManagedLibrary = manifest.Outputs.Contains(
            FoundryOutputKinds.ManagedLibrary,
            StringComparer.Ordinal);
        var build = manifest.ManagedBuild;
        var requestsBridge = manifest.Outputs.Contains(
            FoundryOutputKinds.CphInlineBridge,
            StringComparer.Ordinal);
        var requestsStreamerBotPackage = manifest.Outputs.Contains(
            FoundryOutputKinds.StreamerBotPackage,
            StringComparer.Ordinal);
        if (requestsManagedLibrary)
        {
            ResolveSources(
                build!,
                projectRoot,
                fullProjectPath,
                diagnostics);
        }
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new(null, null, null, diagnostics);
        }

        StreamerBotDefinition? streamerBotDefinition = null;
        if (requestsStreamerBotPackage)
        {
            var definitionPath = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    manifest.TargetDefinition!.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            if (!File.Exists(definitionPath))
            {
                diagnostics.Add(Error(
                    "CFB0010",
                    $"The Streamer.bot target definition '{manifest.TargetDefinition}' does not exist.",
                    fullProjectPath,
                    "$.targetDefinition",
                    "Create the definition file or correct targetDefinition."));
                return new(null, null, null, diagnostics);
            }

            try
            {
                var definitionJson = await File.ReadAllTextAsync(
                    definitionPath,
                    cancellationToken).ConfigureAwait(false);
                var definitionResult = StreamerBotDefinitionLoader.Load(definitionJson);
                if (!definitionResult.IsSuccess)
                {
                    diagnostics.Add(Error(
                        "CFB0011",
                        $"The Streamer.bot target definition is invalid: {string.Join(" ", definitionResult.Errors)}",
                        definitionPath,
                        "$",
                        "Correct the structured Streamer.bot definition and try again."));
                    return new(null, null, null, diagnostics);
                }

                streamerBotDefinition = definitionResult.Definition;
                foreach (var item in StreamerBotDefinitionDiagnostics.Analyze(
                             streamerBotDefinition!, manifest.Target?.Profile))
                {
                    diagnostics.Add(new(
                        item.Code,
                        item.Severity == StreamerBotDefinitionDiagnosticSeverity.Error
                            ? FoundryDiagnosticSeverity.Error
                            : FoundryDiagnosticSeverity.Warning,
                        item.Message,
                        new FoundryDiagnosticLocation(definitionPath, item.Path),
                        item.Severity == StreamerBotDefinitionDiagnosticSeverity.Error
                            ? "Correct the highlighted Streamer.bot definition item and build again."
                            : "Review the highlighted workflow before publishing."));
                }
                if (diagnostics.Any(diagnostic => diagnostic.IsError))
                    return new(null, null, null, diagnostics);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    "CFB0010",
                    $"The Streamer.bot target definition could not be read: {exception.Message}",
                    definitionPath,
                    "$",
                    "Check access to the target definition and try again."));
                return new(null, null, null, diagnostics);
            }
        }

        var outputRoot = Path.Combine(projectRoot, "build");
        var managedOutput = Path.Combine(outputRoot, "managed");
        var intermediateOutput = Path.Combine(outputRoot, "obj", "managed");
        var bridgeOutput = Path.Combine(outputRoot, "bridge");
        var bridgeIntermediate = Path.Combine(outputRoot, "obj", "bridge");
        var streamerBotOutput = Path.Combine(outputRoot, "streamerbot");
        var generatedProjectPath = Path.Combine(
            intermediateOutput,
            "Foundry.Managed.csproj");

        try
        {
            if (ContainsReparsePoint(projectRoot, managedOutput) ||
                ContainsReparsePoint(projectRoot, intermediateOutput) ||
                ContainsReparsePoint(projectRoot, bridgeOutput) ||
                ContainsReparsePoint(projectRoot, bridgeIntermediate) ||
                ContainsReparsePoint(projectRoot, streamerBotOutput))
            {
                diagnostics.Add(Error(
                    "CFB0003",
                    "Foundry will not clean a generated build path that contains a file-system link.",
                    fullProjectPath,
                    "$.managedBuild",
                    "Remove the build directory link and try again."));
                return new(outputRoot, null, null, diagnostics);
            }

            ResetDirectory(managedOutput, requestsManagedLibrary);
            ResetDirectory(intermediateOutput, requestsManagedLibrary);
            ResetDirectory(bridgeOutput, requestsBridge);
            ResetDirectory(bridgeIntermediate, requestsBridge);
            ResetDirectory(streamerBotOutput, requestsStreamerBotPackage);
            Directory.CreateDirectory(outputRoot);
            if (requestsManagedLibrary)
            {
                await FoundryManagedProjectWriter.WriteAsync(
                    manifest,
                    projectRoot,
                    generatedProjectPath,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                "CFB0003",
                $"Foundry could not prepare the generated build directory: {exception.Message}",
                fullProjectPath,
                "$.managedBuild",
                "Check access to the project build directory and try again."));
            return new(outputRoot, null, null, diagnostics);
        }

        string? assemblyPath = null;
        if (requestsManagedLibrary)
        {
            BuildProcessResult processResult;
            try
            {
                processResult = await processRunner.RunAsync(
                    CreateBuildRequest(projectRoot, generatedProjectPath, managedOutput),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                diagnostics.Add(Error(
                    "CFB0004",
                    $"The managed build process could not start: {exception.Message}",
                    fullProjectPath,
                    "$.managedBuild",
                    "Verify that the pinned .NET SDK is installed and available."));
                return new(outputRoot, null, null, diagnostics);
            }

            diagnostics.AddRange(ParseCompilerDiagnostics(processResult));
            if (processResult.ExitCode != 0)
            {
                diagnostics.Add(new(
                    "CFB0005",
                    FoundryDiagnosticSeverity.Error,
                    $"Managed compilation failed with exit code {processResult.ExitCode}.",
                    new FoundryDiagnosticLocation(fullProjectPath, "$.managedBuild"),
                    "Correct the reported build diagnostics and try again.",
                    CreateBuildDetails(processResult)));
                return new(outputRoot, null, null, diagnostics);
            }

            assemblyPath = Path.Combine(managedOutput, $"{build!.AssemblyName}.dll");
            if (!File.Exists(assemblyPath))
            {
                diagnostics.Add(Error(
                    "CFB0006",
                    $"Managed compilation did not produce the expected assembly '{build.AssemblyName}.dll'.",
                    fullProjectPath,
                    "$.managedBuild.assemblyName",
                    "Review the build output and assemblyName setting."));
                return new(outputRoot, null, null, diagnostics);
            }
        }

        string? bridgePath = null;
        if (requestsBridge)
        {
            bridgePath = Path.Combine(bridgeOutput, "CPHInline.cs");

            try
            {
                await CphInlineBridgeGenerator.WriteAsync(
                    manifest.CphInlineBridge!,
                    bridgePath,
                    cancellationToken).ConfigureAwait(false);
                await CphInlineBridgeVerificationWriter.WriteAsync(
                    build!,
                    bridgeIntermediate,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    "CFB0008",
                    $"The CPHInline bridge could not be written: {exception.Message}",
                    fullProjectPath,
                    "$.cphInlineBridge",
                    "Check access to the project build directory and try again."));
                return new(outputRoot, null, null, diagnostics);
            }

            BuildProcessResult bridgeVerificationResult;
            try
            {
                bridgeVerificationResult = await processRunner.RunAsync(
                    CreateBridgeVerificationRequest(projectRoot, bridgeIntermediate),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                diagnostics.Add(Error(
                    "CFB0004",
                    $"The bridge verification process could not start: {exception.Message}",
                    fullProjectPath,
                    "$.cphInlineBridge",
                    "Verify that the pinned .NET SDK is installed and available."));
                return new(outputRoot, null, null, diagnostics);
            }

            diagnostics.AddRange(ParseCompilerDiagnostics(bridgeVerificationResult));
            if (bridgeVerificationResult.ExitCode != 0)
            {
                diagnostics.Add(new(
                    "CFB0009",
                    FoundryDiagnosticSeverity.Error,
                    $"CPHInline bridge contract verification failed with exit code {bridgeVerificationResult.ExitCode}.",
                    new FoundryDiagnosticLocation(fullProjectPath, "$.cphInlineBridge"),
                    "Ensure the configured static entry point implements args-log-v1.",
                    CreateBuildDetails(bridgeVerificationResult)));
                return new(outputRoot, null, null, diagnostics);
            }
        }

        string? streamerBotPackagePath = null;
        string? streamerBotReportPath = null;
        string? streamerBotImportReportPath = null;
        string? streamerBotPortabilityReportPath = null;
        if (requestsStreamerBotPackage)
        {
            streamerBotPackagePath = Path.Combine(
                streamerBotOutput,
                $"{manifest.Id}.streamerbot");
            streamerBotReportPath = Path.Combine(
                streamerBotOutput,
                "package-report.json");
            streamerBotPortabilityReportPath = Path.Combine(
                streamerBotOutput,
                "portability-report.json");

            try
            {
                StreamerBotExportArtifact export;
                if (streamerBotDefinition!.Import is not null)
                {
                    export = await StreamerBotPreservedPayloadAdapter.EncodeAsync(
                        streamerBotDefinition,
                        projectRoot,
                        manifest.Id,
                        manifest.Name,
                        manifest.Version,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (bridgePath is null)
                    {
                        throw new InvalidOperationException(
                            "A source-authored Streamer.bot package requires a CPHInline bridge; package-only builds are supported for imported preserved payloads.");
                    }
                    var bridgeSource = await File.ReadAllTextAsync(
                        bridgePath,
                        cancellationToken).ConfigureAwait(false);
                    export = StreamerBotStableV23Adapter.Encode(
                        streamerBotDefinition,
                        manifest.Id,
                        manifest.Name,
                        manifest.Version,
                        bridgeSource);
                }
                await File.WriteAllTextAsync(
                    streamerBotPackagePath,
                    export.ImportCode + "\n",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    streamerBotReportPath,
                    StreamerBotStableV23Adapter.SerializeReport(export.Report),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    streamerBotPortabilityReportPath,
                    StreamerBotPortabilityService.Serialize(
                        StreamerBotPortabilityService.CreateReport(streamerBotDefinition)),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken).ConfigureAwait(false);
                var sourceImportReport = Path.Combine(projectRoot, "streamerbot", "import-report.json");
                if (streamerBotDefinition.Import is not null && File.Exists(sourceImportReport))
                {
                    streamerBotImportReportPath = Path.Combine(streamerBotOutput, "import-report.json");
                    File.Copy(sourceImportReport, streamerBotImportReportPath, overwrite: true);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or InvalidOperationException)
            {
                diagnostics.Add(Error(
                    "CFB0012",
                    $"The Streamer.bot package could not be generated: {exception.Message}",
                    fullProjectPath,
                    "$.targetDefinition",
                    streamerBotDefinition?.Import is not null
                        ? "Review the imported definition in Streamer.bot Designer, resolve absolute references or paths, save, and try again."
                        : "Correct the target definition and try again."));
                return new(outputRoot, null, null, diagnostics);
            }
        }

        try
        {
            var packageIntermediate = await CreatePackageIntermediateAsync(
                manifest,
                assemblyPath,
                bridgePath,
                streamerBotPackagePath,
                streamerBotReportPath,
                streamerBotImportReportPath,
                streamerBotPortabilityReportPath,
                cancellationToken).ConfigureAwait(false);
            var packageIntermediatePath = Path.Combine(outputRoot, "package-ir.json");
            await WritePackageIntermediateAsync(
                packageIntermediate,
                packageIntermediatePath,
                cancellationToken).ConfigureAwait(false);

            return new(
                outputRoot,
                packageIntermediatePath,
                packageIntermediate,
                diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                "CFB0007",
                $"The package intermediate representation could not be written: {exception.Message}",
                fullProjectPath,
                "$.managedBuild",
                "Check access to the project build directory and try again."));
            return new(outputRoot, null, null, diagnostics);
        }
    }

    private static BuildProcessRequest CreateBuildRequest(
        string projectRoot,
        string generatedProjectPath,
        string managedOutput)
    {
        string[] arguments =
        [
            "build",
            generatedProjectPath,
            "--configuration",
            "Release",
            "--output",
            managedOutput,
            "--nologo",
            "--verbosity:minimal",
            "--property:UseSharedCompilation=false",
        ];
        return new("dotnet", projectRoot, arguments);
    }

    private static BuildProcessRequest CreateBridgeVerificationRequest(
        string projectRoot,
        string bridgeIntermediate)
    {
        string[] arguments =
        [
            "build",
            Path.Combine(bridgeIntermediate, "Foundry.BridgeVerify.csproj"),
            "--configuration",
            "Release",
            "--output",
            Path.Combine(bridgeIntermediate, "output"),
            "--nologo",
            "--verbosity:minimal",
            "--property:UseSharedCompilation=false",
        ];
        return new("dotnet", projectRoot, arguments);
    }

    private static void ResolveSources(
        FoundryManagedBuild build,
        string projectRoot,
        string projectPath,
        List<FoundryDiagnostic> diagnostics)
    {
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(projectRoot) +
            Path.DirectorySeparatorChar;
        for (var index = 0; index < build.Sources.Count; index++)
        {
            var source = build.Sources[index];
            var location = new FoundryDiagnosticLocation(
                projectPath,
                $"$.managedBuild.sources[{index}]");
            string fullSourcePath;

            try
            {
                fullSourcePath = Path.GetFullPath(
                    Path.Combine(
                        projectRoot,
                        source.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                diagnostics.Add(new(
                    "CFB0002",
                    FoundryDiagnosticSeverity.Error,
                    $"Managed source '{source}' could not be resolved: {exception.Message}",
                    location,
                    "Use a valid project-relative source path."));
                continue;
            }

            if (!fullSourcePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new(
                    "CFB0002",
                    FoundryDiagnosticSeverity.Error,
                    $"Managed source '{source}' resolves outside the project directory.",
                    location,
                    "Use a project-relative source path."));
            }
            else if (!File.Exists(fullSourcePath))
            {
                diagnostics.Add(new(
                    "CFB0002",
                    FoundryDiagnosticSeverity.Error,
                    $"Managed source '{source}' does not exist.",
                    location,
                    "Create the source file or correct the manifest path."));
            }
        }
    }

    private static FoundryDiagnostic[] ParseCompilerDiagnostics(BuildProcessResult result)
    {
        var diagnostics = new List<FoundryDiagnostic>();
        var lines = string.Concat(
                result.StandardOutput,
                Environment.NewLine,
                result.StandardError)
            .Split(
                ["\r\n", "\n"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var match = CompilerDiagnosticPattern.Match(line);
            if (!match.Success ||
                !long.TryParse(match.Groups["line"].Value, out var lineNumber) ||
                !long.TryParse(match.Groups["column"].Value, out var columnNumber))
            {
                continue;
            }

            var severity = string.Equals(
                    match.Groups["severity"].Value,
                    "error",
                    StringComparison.OrdinalIgnoreCase)
                ? FoundryDiagnosticSeverity.Error
                : FoundryDiagnosticSeverity.Warning;
            diagnostics.Add(new(
                match.Groups["code"].Value,
                severity,
                match.Groups["message"].Value,
                new FoundryDiagnosticLocation(
                    match.Groups["file"].Value,
                    Line: lineNumber,
                    Column: columnNumber)));
        }

        return diagnostics
            .Distinct()
            .ToArray();
    }

    private static async Task<FoundryPackageIntermediate> CreatePackageIntermediateAsync(
        FoundryProjectManifest manifest,
        string? assemblyPath,
        string? bridgePath,
        string? streamerBotPackagePath,
        string? streamerBotReportPath,
        string? streamerBotImportReportPath,
        string? streamerBotPortabilityReportPath,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<FoundryPackageArtifact>();
        if (assemblyPath is not null)
        {
            artifacts.Add(await CreateArtifactAsync(
                FoundryPackageArtifactKinds.ManagedAssembly,
                $"managed/{Path.GetFileName(assemblyPath)}",
                assemblyPath,
                cancellationToken).ConfigureAwait(false));
        }

        if (bridgePath is not null)
        {
            artifacts.Add(await CreateArtifactAsync(
                FoundryPackageArtifactKinds.CphInlineBridge,
                CphInlineBridgeGenerator.RelativeOutputPath,
                bridgePath,
                cancellationToken).ConfigureAwait(false));
        }

        if (streamerBotPackagePath is not null)
        {
            artifacts.Add(await CreateArtifactAsync(
                FoundryPackageArtifactKinds.StreamerBotPackage,
                $"streamerbot/{Path.GetFileName(streamerBotPackagePath)}",
                streamerBotPackagePath,
                cancellationToken).ConfigureAwait(false));
        }

        if (streamerBotReportPath is not null)
        {
            artifacts.Add(await CreateArtifactAsync(
                FoundryPackageArtifactKinds.StreamerBotPackageReport,
                "streamerbot/package-report.json",
                streamerBotReportPath,
                cancellationToken).ConfigureAwait(false));
        }

        if (streamerBotImportReportPath is not null)
        {
            artifacts.Add(await CreateArtifactAsync(
                FoundryPackageArtifactKinds.StreamerBotImportReport,
                "streamerbot/import-report.json",
                streamerBotImportReportPath,
                cancellationToken).ConfigureAwait(false));
        }

        if (streamerBotPortabilityReportPath is not null)
        {
            artifacts.Add(await CreateArtifactAsync(
                FoundryPackageArtifactKinds.StreamerBotPortabilityReport,
                "streamerbot/portability-report.json",
                streamerBotPortabilityReportPath,
                cancellationToken).ConfigureAwait(false));
        }

        return new()
        {
            Project = new(manifest.Id, manifest.Name, manifest.Version),
            Target = new(
                manifest.Target!.Provider,
                manifest.Target.Profile,
                manifest.ManagedBuild?.TargetFramework ?? "streamerbot-package",
                CphCatalogueMetadata.Revision),
            Artifacts = artifacts,
        };
    }

    private static async Task<FoundryPackageArtifact> CreateArtifactAsync(
        string kind,
        string relativePath,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new(
            kind,
            relativePath,
            stream.Length,
            Convert.ToHexStringLower(hash));
    }

    private static async Task WritePackageIntermediateAsync(
        FoundryPackageIntermediate packageIntermediate,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            packageIntermediate,
            PackageSerializerOptions);
        var temporaryPath = $"{destinationPath}.tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            $"{json}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, destinationPath, overwrite: true);
    }

    private static void ResetDirectory(string path, bool create = true)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        if (create)
        {
            Directory.CreateDirectory(path);
        }
    }

    private static bool ContainsReparsePoint(string projectRoot, string targetPath)
    {
        var relativePath = Path.GetRelativePath(projectRoot, targetPath);
        var currentPath = projectRoot;

        foreach (var segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (Directory.Exists(currentPath) &&
                File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private static FoundryDiagnostic Error(
        string code,
        string message,
        string projectPath,
        string jsonPath,
        string suggestedFix) => new(
            code,
            FoundryDiagnosticSeverity.Error,
            message,
            new FoundryDiagnosticLocation(projectPath, jsonPath),
            suggestedFix);

    private static string CreateBuildDetails(BuildProcessResult result)
    {
        var details = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput.Trim(), result.StandardError.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return details.Length <= MaximumBuildDetailsCharacters
            ? details
            : details[..MaximumBuildDetailsCharacters];
    }
}
