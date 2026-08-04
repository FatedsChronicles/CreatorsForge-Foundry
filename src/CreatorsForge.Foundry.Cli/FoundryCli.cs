using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Build.ObsStudio;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Testing;
using CreatorsForge.Foundry.NativeTestHost;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.Cli;

public static class FoundryCli
{
    public const int SuccessExitCode = 0;
    public const int DiagnosticErrorExitCode = 1;
    public const int UsageErrorExitCode = 2;
    public const int CancelledExitCode = 130;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        IBuildProcessRunner? buildProcessRunner = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (args.Count >= 3 &&
            string.Equals(args[0], "sdk", StringComparison.OrdinalIgnoreCase))
        {
            return await RunSdkCommandAsync(
                args,
                standardOutput,
                standardError,
                cancellationToken).ConfigureAwait(false);
        }

        var isValidateWorkspace = args.Count == 2 && string.Equals(args[0], "validate-workspace", StringComparison.OrdinalIgnoreCase);
        var isBuildWorkspace = args.Count == 2 && string.Equals(args[0], "build-workspace", StringComparison.OrdinalIgnoreCase);
        if (isValidateWorkspace || isBuildWorkspace)
        {
            var workspace = await FoundryWorkspaceSetService.LoadAsync(args[1], cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(workspace.Diagnostics, standardError).ConfigureAwait(false);
            if (!workspace.IsSuccess) return DiagnosticErrorExitCode;
            if (isValidateWorkspace)
            {
                await standardOutput.WriteLineAsync($"Workspace is valid: {workspace.Value!.Manifest.Name} ({workspace.Value.Projects.Count} projects)").ConfigureAwait(false);
                return SuccessExitCode;
            }

            var orchestrator = new FoundryBuildOrchestrator(buildProcessRunner);
            foreach (var project in workspace.Value!.Projects)
            {
                var result = await orchestrator.BuildAsync(project.Manifest, project.ProjectPath, cancellationToken).ConfigureAwait(false);
                await WriteDiagnosticsAsync(result.Diagnostics, standardError).ConfigureAwait(false);
                if (!result.IsSuccess) return DiagnosticErrorExitCode;
                await standardOutput.WriteLineAsync($"[PASSED] {project.Manifest.Name} {project.Manifest.Version}").ConfigureAwait(false);
            }
            await standardOutput.WriteLineAsync($"Workspace build succeeded: {workspace.Value.Manifest.Name}").ConfigureAwait(false);
            return SuccessExitCode;
        }

        if (args.Count is 2 or 3 && string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
        {
            var apply = args.Count == 3 && string.Equals(args[2], "--apply", StringComparison.OrdinalIgnoreCase);
            if (args.Count == 3 && !apply) { await WriteUsageAsync(standardError).ConfigureAwait(false); return UsageErrorExitCode; }
            var inspection = await FoundryProjectMigrationService.InspectAsync(args[1], cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(inspection.Diagnostics, standardError).ConfigureAwait(false);
            if (!inspection.IsSuccess) return DiagnosticErrorExitCode;
            if (!inspection.Plan!.IsRequired)
            {
                await standardOutput.WriteLineAsync("Project already uses the current schema.").ConfigureAwait(false);
                return SuccessExitCode;
            }
            foreach (var change in inspection.Plan.Changes) await standardOutput.WriteLineAsync($"- {change}").ConfigureAwait(false);
            await standardOutput.WriteLineAsync($"Backup: {inspection.Plan.BackupPath}").ConfigureAwait(false);
            if (!apply)
            {
                await standardOutput.WriteLineAsync("Preview only. Re-run with --apply to migrate.").ConfigureAwait(false);
                return SuccessExitCode;
            }
            var migration = await FoundryProjectMigrationService.MigrateAsync(args[1], cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(migration.Diagnostics, standardError).ConfigureAwait(false);
            if (!migration.IsSuccess) return DiagnosticErrorExitCode;
            await standardOutput.WriteLineAsync("Migration succeeded: schema 0 -> schema 1").ConfigureAwait(false);
            return SuccessExitCode;
        }

        if (args.Count == 4 && string.Equals(args[0], "template", StringComparison.OrdinalIgnoreCase) && string.Equals(args[1], "export", StringComparison.OrdinalIgnoreCase))
        {
            var project = await FoundryWorkspaceService.OpenAsync(args[2], cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(project.Diagnostics, standardError).ConfigureAwait(false);
            if (!project.IsSuccess) return DiagnosticErrorExitCode;
            var templateDiagnostics = await FoundryTemplateInterchangeService.ExportAsync(project.Value!, args[3], cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(templateDiagnostics, standardError).ConfigureAwait(false);
            if (templateDiagnostics.Any(item => item.IsError)) return DiagnosticErrorExitCode;
            await standardOutput.WriteLineAsync($"Template exported: {Path.GetFullPath(args[3])}").ConfigureAwait(false);
            return SuccessExitCode;
        }

        if (TryParseTemplateImport(args, out var importRequest))
        {
            var result = await FoundryTemplateInterchangeService.ImportAsync(importRequest!, cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(result.Diagnostics, standardError).ConfigureAwait(false);
            if (!result.IsSuccess) return DiagnosticErrorExitCode;
            await standardOutput.WriteLineAsync($"Template imported: {result.Value!.ProjectPath}").ConfigureAwait(false);
            return SuccessExitCode;
        }

        if (args.Count == 3 && string.Equals(args[0], "version", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = await FoundryWorkspaceService.OpenAsync(args[1], cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(workspace.Diagnostics, standardError).ConfigureAwait(false);
            if (!workspace.IsSuccess) return DiagnosticErrorExitCode;
            var result = await FoundryPublishingService.SetVersionAsync(
                workspace.Value!, args[2], cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(result.Diagnostics, standardError).ConfigureAwait(false);
            if (!result.IsSuccess) return DiagnosticErrorExitCode;
            await standardOutput.WriteLineAsync($"Version updated: {result.Value!.Manifest.Version}").ConfigureAwait(false);
            return SuccessExitCode;
        }

        var isValidate =
            args.Count == 2 &&
            string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase);
        var isBuild =
            args.Count == 2 &&
            string.Equals(args[0], "build", StringComparison.OrdinalIgnoreCase);
        var isRelease =
            args.Count == 2 &&
            string.Equals(args[0], "release", StringComparison.OrdinalIgnoreCase);
        var isPublish =
            args.Count == 2 &&
            string.Equals(args[0], "publish", StringComparison.OrdinalIgnoreCase);
        var isPublishValidate =
            args.Count == 3 &&
            string.Equals(args[0], "publish", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "validate", StringComparison.OrdinalIgnoreCase);
        var isTest = TryParseTestCommand(args, out var obsRoot);
        var isTestMatrix = TryParseTestMatrixCommand(args, out var matrixObsRoots);

        if (!isValidate && !isBuild && !isRelease && !isPublish && !isPublishValidate && !isTest && !isTestMatrix)
        {
            await WriteUsageAsync(standardError).ConfigureAwait(false);
            return UsageErrorExitCode;
        }

        var loadResult = await FoundryProjectLoader.LoadAsync(
            isPublishValidate ? args[2] : args[1],
            cancellationToken).ConfigureAwait(false);
        var diagnostics = new List<FoundryDiagnostic>(loadResult.Diagnostics);

        if (loadResult.Manifest is not null)
        {
            diagnostics.AddRange(
                FoundryProjectValidator.Validate(
                    loadResult.Manifest,
                    loadResult.ProjectPath));
        }

        if (diagnostics.Any(diagnostic => diagnostic.IsError) ||
            loadResult.Manifest is null)
        {
            await WriteDiagnosticsAsync(diagnostics, standardError).ConfigureAwait(false);
            return DiagnosticErrorExitCode;
        }

        if (isBuild || isRelease || isPublish || isPublishValidate || isTest || isTestMatrix)
        {
            var orchestrator = new FoundryBuildOrchestrator(buildProcessRunner);
            var buildResult = await orchestrator.BuildAsync(
                loadResult.Manifest,
                loadResult.ProjectPath!,
                cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticsAsync(
                buildResult.Diagnostics,
                standardError).ConfigureAwait(false);

            if (!buildResult.IsSuccess)
            {
                return DiagnosticErrorExitCode;
            }

            if (isPublishValidate)
            {
                var readiness = FoundryPublishingReadinessService.Inspect(
                    loadResult.Manifest,
                    loadResult.ProjectPath!,
                    buildResult);
                await WriteDiagnosticsAsync(readiness.Diagnostics, standardError).ConfigureAwait(false);
                foreach (var item in readiness.Checklist)
                {
                    await standardOutput.WriteLineAsync(
                        $"[{(item.Passed ? "PASSED" : item.Required ? "FAILED" : "OPTIONAL")}] {item.Name}: {item.Details}")
                        .ConfigureAwait(false);
                }
                await standardOutput.WriteLineAsync(readiness.IsReady
                    ? "Publishing validation passed."
                    : "Publishing validation failed.").ConfigureAwait(false);
                return readiness.IsReady ? SuccessExitCode : DiagnosticErrorExitCode;
            }

            if (isTestMatrix)
            {
                var projectRoot = Path.GetDirectoryName(loadResult.ProjectPath!)!;
                var isObs = string.Equals(loadResult.Manifest.Target?.Provider, "obsstudio", StringComparison.Ordinal);
                var artifactKind = isObs
                    ? FoundryPackageArtifactKinds.NativeObsPlugin
                    : FoundryPackageArtifactKinds.ManagedAssembly;
                var artifact = buildResult.PackageIntermediate!.Artifacts.SingleOrDefault(item =>
                    item.Kind == artifactKind);
                if (artifact is null || (!isObs && matrixObsRoots.Count > 0))
                {
                    await standardError.WriteLineAsync(
                        "foundry: error CFT3002: Matrix runtime inputs do not match the built provider artifact.")
                        .ConfigureAwait(false);
                    return DiagnosticErrorExitCode;
                }

                var matrix = await FoundryCompatibilityMatrixRunner.RunAsync(
                    new(
                        loadResult.Manifest,
                        loadResult.ProjectPath!,
                        Path.Combine(projectRoot, "build", artifact.Path.Replace('/', Path.DirectorySeparatorChar)),
                        matrixObsRoots,
                        isObs ? typeof(NativeTestHostMarker).Assembly.Location : null),
                    cancellationToken).ConfigureAwait(false);
                await WriteDiagnosticsAsync(matrix.Diagnostics, standardError).ConfigureAwait(false);
                foreach (var cell in matrix.Cells)
                {
                    await standardOutput.WriteLineAsync(
                        $"[{cell.Outcome.ToString().ToUpperInvariant()}] {cell.Profile} ({cell.RuntimeVersion})")
                        .ConfigureAwait(false);
                }

                await standardOutput.WriteLineAsync("Compatibility matrix: build/test-results/compatibility-matrix.json")
                    .ConfigureAwait(false);
                await standardOutput.WriteLineAsync(
                    $"Matrix {matrix.Outcome.ToString().ToLowerInvariant()}: {matrix.Cells.Count(item => item.Outcome == FoundryTestOutcome.Passed)} passed, {matrix.Cells.Count(item => item.Outcome == FoundryTestOutcome.Failed)} failed, {matrix.Cells.Count(item => item.Outcome == FoundryTestOutcome.Error)} errors.")
                    .ConfigureAwait(false);
                return matrix.IsSuccess ? SuccessExitCode : DiagnosticErrorExitCode;
            }

            if (isTest)
            {
                var projectRoot = Path.GetDirectoryName(loadResult.ProjectPath!)!;
                var isObs = string.Equals(loadResult.Manifest.Target?.Provider, "obsstudio", StringComparison.Ordinal);
                var artifactKind = isObs
                    ? FoundryPackageArtifactKinds.NativeObsPlugin
                    : FoundryPackageArtifactKinds.ManagedAssembly;
                var artifact = buildResult.PackageIntermediate!.Artifacts.SingleOrDefault(item =>
                    item.Kind == artifactKind);
                if (artifact is null || (isObs && string.IsNullOrWhiteSpace(obsRoot)) || (!isObs && obsRoot is not null))
                {
                    await standardError.WriteLineAsync(
                        "foundry: error CFT2004: Test runtime inputs do not match the built provider artifact.")
                        .ConfigureAwait(false);
                    return DiagnosticErrorExitCode;
                }

                var testResult = await FoundryProviderTestOrchestrator.RunAsync(
                    new(
                        loadResult.Manifest,
                        loadResult.ProjectPath!,
                        Path.Combine(projectRoot, "build", artifact.Path.Replace('/', Path.DirectorySeparatorChar)),
                        obsRoot,
                        isObs ? typeof(NativeTestHostMarker).Assembly.Location : null),
                    cancellationToken).ConfigureAwait(false);
                await WriteDiagnosticsAsync(testResult.Diagnostics, standardError).ConfigureAwait(false);
                foreach (var testCase in testResult.Cases)
                {
                    await standardOutput.WriteLineAsync(
                        $"[{testCase.Outcome.ToString().ToUpperInvariant()}] {testCase.Name}")
                        .ConfigureAwait(false);
                }

                await standardOutput.WriteLineAsync(
                    $"Test result: build/{Path.GetRelativePath(Path.Combine(projectRoot, "build"), testResult.ResultPath!).Replace('\\', '/')}")
                    .ConfigureAwait(false);
                await standardOutput.WriteLineAsync(
                    $"Tests {testResult.Outcome.ToString().ToLowerInvariant()}: {testResult.Cases.Count(item => item.Outcome == FoundryTestOutcome.Passed)} passed, {testResult.Cases.Count(item => item.Outcome == FoundryTestOutcome.Failed)} failed, {testResult.Cases.Count(item => item.Outcome == FoundryTestOutcome.Error)} errors.")
                    .ConfigureAwait(false);
                return testResult.IsSuccess ? SuccessExitCode : DiagnosticErrorExitCode;
            }

            if (isRelease || isPublish)
            {
                var packager = new FoundryReleasePackager();
                var releaseResult = isPublish
                    ? await packager.CreatePublishingAsync(
                        loadResult.Manifest, loadResult.ProjectPath!, buildResult, cancellationToken).ConfigureAwait(false)
                    : await packager.CreateAsync(
                        loadResult.Manifest, loadResult.ProjectPath!, buildResult, cancellationToken).ConfigureAwait(false);
                await WriteDiagnosticsAsync(
                    releaseResult.Diagnostics.Except(buildResult.Diagnostics),
                    standardError).ConfigureAwait(false);
                if (!releaseResult.IsSuccess)
                {
                    return DiagnosticErrorExitCode;
                }

                var projectRoot = Path.GetDirectoryName(loadResult.ProjectPath!)!;
                await standardOutput.WriteLineAsync(
                    $"{(isPublish ? "Publish" : "Release")} succeeded: {loadResult.Manifest.Name} {loadResult.Manifest.Version}")
                    .ConfigureAwait(false);
                await standardOutput.WriteLineAsync(
                    $"Release bundle: {Path.GetRelativePath(projectRoot, releaseResult.ReleaseDirectory!).Replace('\\', '/')}")
                    .ConfigureAwait(false);
                await standardOutput.WriteLineAsync(
                    $"Release archive: {Path.GetRelativePath(projectRoot, releaseResult.ArchivePath!).Replace('\\', '/')}")
                    .ConfigureAwait(false);
                await standardOutput.WriteLineAsync(
                    $"Build manifest: {Path.GetRelativePath(projectRoot, releaseResult.ManifestPath!).Replace('\\', '/')}")
                    .ConfigureAwait(false);
                await standardOutput.WriteLineAsync(
                    $"Reproducibility report: {Path.GetRelativePath(projectRoot, releaseResult.ReproducibilityReportPath!).Replace('\\', '/')}")
                    .ConfigureAwait(false);
                return SuccessExitCode;
            }

            await standardOutput.WriteLineAsync(
                $"Build succeeded: {loadResult.Manifest.Name} {loadResult.Manifest.Version}")
                .ConfigureAwait(false);

            foreach (var artifact in buildResult.PackageIntermediate!.Artifacts)
            {
                var label = artifact.Kind switch
                {
                    "managedAssembly" => "Managed assembly",
                    "cphInlineBridge" => "CPHInline bridge",
                    "streamerBotPackage" => "Streamer.bot package",
                    "streamerBotPackageReport" => "Streamer.bot package report",
                    "nativeObsPlugin" => "Native OBS plugin",
                    "obsPluginPackage" => "OBS plugin package",
                    _ => "Artifact",
                };
                await standardOutput.WriteLineAsync(
                    $"{label}: build/{artifact.Path}").ConfigureAwait(false);
            }

            await standardOutput.WriteLineAsync(
                "Package IR: build/package-ir.json").ConfigureAwait(false);
            return SuccessExitCode;
        }

        await standardOutput.WriteLineAsync(
            $"Project is valid: {loadResult.Manifest.Name} ({loadResult.Manifest.Id}) {loadResult.Manifest.Version}")
            .ConfigureAwait(false);
        return SuccessExitCode;
    }

    private static async Task WriteUsageAsync(TextWriter standardError)
    {
        await standardError.WriteLineAsync(
            "Usage: foundry validate <project.foundryproj>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry build <project.foundryproj>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry release <project.foundryproj>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry publish validate <project.foundryproj>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry publish <project.foundryproj>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry version <project.foundryproj> <major|minor|patch|version>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry test <project.foundryproj> [--obs <installation>]").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry test-matrix <project.foundryproj> [--obs <installation> ...]").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry validate-workspace <workspace.foundryworkspace>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry build-workspace <workspace.foundryworkspace>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry migrate <project.foundryproj> [--apply]").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry template export <project.foundryproj> <output.foundrytemplate>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry template import <template.foundrytemplate> <directory> --name <name> --id <id> --profile <profile>").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry sdk status obsstudio [--cache <directory>]").ConfigureAwait(false);
        await standardError.WriteLineAsync(
            "       foundry sdk install obsstudio [--cache <directory>] [--archives <directory>]").ConfigureAwait(false);
    }

    private static bool TryParseTestCommand(IReadOnlyList<string> args, out string? obsRoot)
    {
        obsRoot = null;
        if (args.Count == 2 && string.Equals(args[0], "test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (args.Count == 4 &&
            string.Equals(args[0], "test", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[2], "--obs", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(args[3]))
        {
            obsRoot = args[3];
            return true;
        }

        return false;
    }

    private static bool TryParseTemplateImport(
        IReadOnlyList<string> args,
        out FoundryTemplateImportRequest? request)
    {
        request = null;
        if (args.Count != 10 || !string.Equals(args[0], "template", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(args[1], "import", StringComparison.OrdinalIgnoreCase)) return false;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 4; index < args.Count; index += 2) values[args[index]] = args[index + 1];
        if (!values.TryGetValue("--name", out var name) || !values.TryGetValue("--id", out var id) ||
            !values.TryGetValue("--profile", out var profile)) return false;
        request = new(args[2], args[3], name, id, profile);
        return true;
    }

    private static bool TryParseTestMatrixCommand(
        IReadOnlyList<string> args,
        out IReadOnlyList<string> obsRoots)
    {
        var roots = new List<string>();
        obsRoots = roots;
        if (args.Count < 2 ||
            !string.Equals(args[0], "test-matrix", StringComparison.OrdinalIgnoreCase) ||
            (args.Count - 2) % 2 != 0)
        {
            return false;
        }

        for (var index = 2; index < args.Count; index += 2)
        {
            if (!string.Equals(args[index], "--obs", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return false;
            }

            roots.Add(args[index + 1]);
        }

        return true;
    }

    private static async Task<int> RunSdkCommandAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(args[2], "obsstudio", StringComparison.OrdinalIgnoreCase) ||
            !TryParseSdkOptions(args, out var cacheRoot, out var archiveDirectory))
        {
            await WriteUsageAsync(standardError).ConfigureAwait(false);
            return UsageErrorExitCode;
        }

        var operation = args[1];
        if (string.Equals(operation, "status", StringComparison.OrdinalIgnoreCase))
        {
            if (archiveDirectory is not null)
            {
                await WriteUsageAsync(standardError).ConfigureAwait(false);
                return UsageErrorExitCode;
            }

            var status = ObsSdkManager.Inspect(cacheRoot);
            await standardOutput.WriteLineAsync(
                status.IsReady
                    ? $"OBS SDK {status.Version} ready: {status.SdkRoot}"
                    : $"OBS SDK {status.Version} not ready: {status.SdkRoot} ({status.Message})")
                .ConfigureAwait(false);
            return status.IsReady ? SuccessExitCode : DiagnosticErrorExitCode;
        }

        if (!string.Equals(operation, "install", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUsageAsync(standardError).ConfigureAwait(false);
            return UsageErrorExitCode;
        }

        try
        {
            var progress = new TextWriterProgress(standardOutput);
            var status = await ObsSdkManager.InstallAsync(
                cacheRoot,
                archiveDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);
            await standardOutput.WriteLineAsync(
                status.IsReady
                    ? $"OBS SDK {status.Version} ready: {status.SdkRoot}"
                    : $"OBS SDK installation incomplete: {status.Message}")
                .ConfigureAwait(false);
            return status.IsReady ? SuccessExitCode : DiagnosticErrorExitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or
                System.Net.Http.HttpRequestException)
        {
            await standardError.WriteLineAsync(
                $"foundry: error CFS1001: OBS SDK installation failed: {exception.Message}")
                .ConfigureAwait(false);
            return DiagnosticErrorExitCode;
        }
    }

    private static bool TryParseSdkOptions(
        IReadOnlyList<string> args,
        out string? cacheRoot,
        out string? archiveDirectory)
    {
        cacheRoot = null;
        archiveDirectory = null;
        for (var index = 3; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count)
            {
                return false;
            }

            if (string.Equals(args[index], "--cache", StringComparison.OrdinalIgnoreCase) &&
                cacheRoot is null)
            {
                cacheRoot = args[index + 1];
            }
            else if (string.Equals(args[index], "--archives", StringComparison.OrdinalIgnoreCase) &&
                     archiveDirectory is null)
            {
                archiveDirectory = args[index + 1];
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private sealed class TextWriterProgress(TextWriter writer) : IProgress<string>
    {
        public void Report(string value) => writer.WriteLine(value);
    }

    private static async Task WriteDiagnosticsAsync(
        IEnumerable<FoundryDiagnostic> diagnostics,
        TextWriter standardError)
    {
        foreach (var diagnostic in diagnostics)
        {
            await standardError.WriteLineAsync(FormatDiagnostic(diagnostic)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(diagnostic.Details))
            {
                foreach (var line in diagnostic.Details.Split(
                    ["\r\n", "\n"],
                    StringSplitOptions.None))
                {
                    await standardError.WriteLineAsync($"  {line}").ConfigureAwait(false);
                }
            }
        }
    }

    private static string FormatDiagnostic(FoundryDiagnostic diagnostic)
    {
        var location = diagnostic.Location;
        var position = location switch
        {
            { Line: not null, Column: not null } =>
                $"{location.FilePath}({location.Line},{location.Column})",
            not null when !string.IsNullOrWhiteSpace(location.JsonPath) =>
                $"{location.FilePath} {location.JsonPath}",
            not null => location.FilePath,
            null => "foundry",
        };
        var severity = diagnostic.Severity.ToString().ToLowerInvariant();
        var suggestedFix = string.IsNullOrWhiteSpace(diagnostic.SuggestedFix)
            ? string.Empty
            : $" Fix: {diagnostic.SuggestedFix}";

        return $"{position}: {severity} {diagnostic.Code}: {diagnostic.Message}{suggestedFix}";
    }
}
