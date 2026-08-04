using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build;

public sealed class FoundryReleasePackager(TimeProvider? timeProvider = null)
{
    private static readonly DateTimeOffset ArchiveTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TimeProvider clock = ResolveTimeProvider(timeProvider);

    private static TimeProvider ResolveTimeProvider(TimeProvider? supplied)
    {
        if (supplied is not null) return supplied;
        var sourceDateEpoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        return long.TryParse(sourceDateEpoch, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(seconds))
            : TimeProvider.System;
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }

    public async Task<FoundryReleaseResult> CreateAsync(
        FoundryProjectManifest project,
        string projectPath,
        FoundryBuildResult build,
        CancellationToken cancellationToken = default)
        => await CreateCoreAsync(project, projectPath, build, requirePublishing: false, cancellationToken).ConfigureAwait(false);

    public async Task<FoundryReleaseResult> CreatePublishingAsync(
        FoundryProjectManifest project,
        string projectPath,
        FoundryBuildResult build,
        CancellationToken cancellationToken = default)
        => await CreateCoreAsync(project, projectPath, build, requirePublishing: true, cancellationToken).ConfigureAwait(false);

    private async Task<FoundryReleaseResult> CreateCoreAsync(
        FoundryProjectManifest project,
        string projectPath,
        FoundryBuildResult build,
        bool requirePublishing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(build);

        var fullProjectPath = Path.GetFullPath(projectPath);
        var diagnostics = FoundryProjectValidator.Validate(project, fullProjectPath).ToList();
        diagnostics.AddRange(build.Diagnostics);
        if (!build.IsSuccess || build.PackageIntermediate is null ||
            diagnostics.Any(item => item.IsError))
        {
            diagnostics.Add(Error(
                "CFR1001",
                "A successful validated build is required before creating a release.",
                fullProjectPath,
                "Build the project successfully, then create the release again."));
            return new(null, null, null, null, diagnostics);
        }

        var projectRoot = Path.GetDirectoryName(fullProjectPath)!;
        var readiness = FoundryPublishingReadinessService.Inspect(project, fullProjectPath, build);
        if (requirePublishing && !readiness.IsReady)
        {
            diagnostics.AddRange(readiness.Diagnostics);
            return new(null, null, null, null, diagnostics);
        }
        var buildRoot = Path.Combine(projectRoot, "build");
        var releaseRoot = Path.Combine(buildRoot, "release");
        var releaseName = $"{CreatePortableName(project.Id)}-{project.Version}";
        var releaseDirectory = Path.Combine(releaseRoot, releaseName);
        var archivePath = Path.Combine(releaseRoot, $"{releaseName}-foundry.zip");
        var artifactSources = new List<(FoundryPackageArtifact Artifact, string Source)>();

        foreach (var artifact in build.PackageIntermediate.Artifacts)
        {
            if (!TryResolveBuildPath(buildRoot, artifact.Path, out var source) ||
                artifactSources.Any(item => string.Equals(
                    item.Artifact.Path,
                    artifact.Path,
                    StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(Error(
                    "CFR1002",
                    $"Release artifact path '{artifact.Path}' is unsafe or duplicated.",
                    fullProjectPath,
                    "Rebuild the project to regenerate a valid package inventory."));
                continue;
            }

            if (!File.Exists(source))
            {
                diagnostics.Add(Error(
                    "CFR1003",
                    $"Release artifact '{artifact.Path}' is missing.",
                    source,
                    "Rebuild the project before creating a release."));
                continue;
            }

            var info = new FileInfo(source);
            var hash = await HashAsync(source, cancellationToken).ConfigureAwait(false);
            if (info.Length != artifact.Size ||
                !string.Equals(hash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    "CFR1004",
                    $"Release artifact '{artifact.Path}' no longer matches package-ir.json.",
                    source,
                    "Rebuild the project; do not release modified build output."));
                continue;
            }

            artifactSources.Add((artifact, source));
        }

        if (diagnostics.Any(item => item.IsError))
        {
            return new(null, null, null, null, diagnostics);
        }

        try
        {
            Directory.CreateDirectory(releaseRoot);
            ResetKnownDirectory(buildRoot, releaseDirectory);
            Directory.CreateDirectory(releaseDirectory);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            var files = new List<FoundryReleaseFile>();
            foreach (var (artifact, source) in artifactSources.OrderBy(
                         item => item.Artifact.Path,
                         StringComparer.Ordinal))
            {
                var destination = ResolveReleasePath(releaseDirectory, artifact.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }

            var signing = requirePublishing
                ? project.Publishing?.Signing ?? new FoundrySigningConfiguration()
                : new FoundrySigningConfiguration();
            var signingResult = await FoundryCodeSigningService.SignReleasePayloadsAsync(
                releaseDirectory,
                signing,
                cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(signingResult.Diagnostics);
            if (!signingResult.IsSuccess)
                return new(null, null, null, null, diagnostics);

            foreach (var (artifact, _) in artifactSources.OrderBy(item => item.Artifact.Path, StringComparer.Ordinal))
            {
                var destination = ResolveReleasePath(releaseDirectory, artifact.Path);
                files.Add(await DescribeAsync(
                    artifact.Kind,
                    artifact.Path.Replace('\\', '/'),
                    destination,
                    cancellationToken).ConfigureAwait(false));
            }

            var packageIrPath = Path.Combine(releaseDirectory, "package-ir.json");
            await WriteJsonAsync(
                packageIrPath,
                build.PackageIntermediate,
                cancellationToken).ConfigureAwait(false);
            files.Add(await DescribeAsync(
                "packageIr",
                "package-ir.json",
                packageIrPath,
                cancellationToken).ConfigureAwait(false));

            var readmePath = Path.Combine(releaseDirectory, "README.md");
            await File.WriteAllTextAsync(
                readmePath,
                CreateReadme(project, build.PackageIntermediate),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            files.Add(await DescribeAsync(
                "readme",
                "README.md",
                readmePath,
                cancellationToken).ConfigureAwait(false));

            if (requirePublishing && project.Publishing is { } publishing)
            {
                foreach (var legalFile in new[] { publishing.LicenseFile, publishing.ChangelogFile }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var source = Path.GetFullPath(Path.Combine(projectRoot, legalFile.Replace('/', Path.DirectorySeparatorChar)));
                    if (!File.Exists(source))
                    {
                        diagnostics.Add(Error("CFR2002", $"Publishing file '{legalFile}' is missing.", source, "Create the declared licence or changelog file."));
                        continue;
                    }
                    var destinationName = Path.GetFileName(legalFile);
                    var destination = Path.Combine(releaseDirectory, destinationName);
                    File.Copy(source, destination, overwrite: false);
                    files.Add(await DescribeAsync(destinationName.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) ? "license" : "changelog", destinationName, destination, cancellationToken).ConfigureAwait(false));
                }
                var checklistPath = Path.Combine(releaseDirectory, "publishing-checklist.json");
                await WriteJsonAsync(checklistPath, new FoundryPublishingChecklistReport
                {
                    PackageName = publishing.PackageName,
                    Version = project.Version,
                    Checklist = readiness.Checklist,
                    Dependencies = readiness.Dependencies,
                }, cancellationToken).ConfigureAwait(false);
                files.Add(await DescribeAsync("publishingChecklist", "publishing-checklist.json", checklistPath, cancellationToken).ConfigureAwait(false));
            }

            if (diagnostics.Any(item => item.IsError))
                return new(null, null, null, null, diagnostics);

            var manifest = new FoundryReleaseManifest
            {
                FoundryVersion = GetFoundryVersion(),
                BuildTimestampUtc = clock.GetUtcNow(),
                Configuration = "Release",
                Project = build.PackageIntermediate.Project,
                Target = build.PackageIntermediate.Target,
                Dependencies = readiness.Dependencies,
                Signing = new(
                    signing.Enabled,
                    signing.Enabled && signingResult.IsSuccess,
                    signing.Enabled ? Path.GetFileName(signing.ToolPath) : null,
                    signing.Enabled ? signing.CertificateThumbprint : null,
                    signingResult.SignedFiles),
                Warnings = diagnostics
                    .Where(item => item.Severity == FoundryDiagnosticSeverity.Warning)
                    .Select(item => new FoundryReleaseWarning(item.Code, item.Message))
                    .ToArray(),
                Validation = new(true, true, true, true, !signing.Enabled || signingResult.IsSuccess),
                Files = files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            };
            var manifestPath = Path.Combine(releaseDirectory, "foundry-build.json");
            await WriteJsonAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);

            await WriteArchiveAsync(
                releaseDirectory,
                archivePath,
                cancellationToken).ConfigureAwait(false);
            await VerifyArchiveAsync(
                archivePath,
                manifest,
                cancellationToken).ConfigureAwait(false);

            var reproducibilityPath = Path.Combine(releaseRoot, $"{releaseName}-reproducibility.json");
            var archiveInfo = new FileInfo(archivePath);
            await WriteJsonAsync(reproducibilityPath, new FoundryReproducibilityReport
            {
                ProjectId = project.Id,
                Version = project.Version,
                Archive = Path.GetFileName(archivePath),
                ArchiveSize = archiveInfo.Length,
                ArchiveSha256 = await HashAsync(archivePath, cancellationToken).ConfigureAwait(false),
                BuildManifestSha256 = await HashAsync(manifestPath, cancellationToken).ConfigureAwait(false),
                ReproductionCommand = $"foundry {(requirePublishing ? "publish" : "release")} {Path.GetFileName(fullProjectPath)}",
            }, cancellationToken).ConfigureAwait(false);

            return new(
                releaseDirectory,
                archivePath,
                manifestPath,
                manifest,
                diagnostics)
            {
                ReproducibilityReportPath = reproducibilityPath,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException)
        {
            diagnostics.Add(Error(
                "CFR1005",
                $"The release package could not be assembled or verified: {exception.Message}",
                fullProjectPath,
                "Check the build directory and available disk space, then try again."));
            return new(null, null, null, null, diagnostics);
        }
    }

    private static async Task WriteArchiveAsync(
        string releaseDirectory,
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in Directory.EnumerateFiles(releaseDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(item => Path.GetRelativePath(releaseDirectory, item), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(releaseDirectory, file).Replace('\\', '/');
            var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
            entry.LastWriteTime = ArchiveTimestamp;
            await using var entryStream = entry.Open();
            await using var source = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            await source.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task VerifyArchiveAsync(
        string archivePath,
        FoundryReleaseManifest manifest,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.ToDictionary(
            item => item.FullName,
            StringComparer.OrdinalIgnoreCase);
        if (!entries.ContainsKey("foundry-build.json") ||
            entries.Count != manifest.Files.Count + 1)
        {
            throw new InvalidDataException("The release archive inventory is incomplete.");
        }

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(file.Path, out var entry) || entry.Length != file.Size)
            {
                throw new InvalidDataException($"Archive entry '{file.Path}' is missing or has the wrong size.");
            }

            await using var stream = entry.Open();
            var hash = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry '{file.Path}' failed hash verification.");
            }
        }
    }

    private static string CreateReadme(
        FoundryProjectManifest project,
        FoundryPackageIntermediate package)
    {
        var header = $"# {project.Name} {project.Version}\n\n" +
            $"Target: `{package.Target.Provider}` / `{package.Target.Profile}`\n\n" +
            "This release was built and hash-verified by Creators Forge Foundry. " +
            "Review `foundry-build.json` before installation.\n\n";
        if (string.Equals(package.Target.Provider, "streamerbot", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = package.Artifacts.Single(item =>
                item.Kind == FoundryPackageArtifactKinds.ManagedAssembly).Path;
            var bridge = package.Artifacts.FirstOrDefault(item =>
                item.Kind == FoundryPackageArtifactKinds.CphInlineBridge)?.Path;
            var import = package.Artifacts.FirstOrDefault(item =>
                item.Kind == FoundryPackageArtifactKinds.StreamerBotPackage)?.Path;
            return header + "## Install in Streamer.bot\n\n" +
                $"1. Copy `{assembly}` into the selected Streamer.bot `dlls` directory.\n" +
                $"2. Add `{Path.GetFileName(assembly)}` as a compiler reference.\n" +
                (import is null ? string.Empty : $"3. Open Streamer.bot Import and paste the contents of `{import}`.\n") +
                (bridge is null ? string.Empty : $"4. Review the imported inline action against `{bridge}` and compile it.\n") +
                "5. Run the action once and confirm the expected Foundry log message.\n\n" +
                "Use Foundry's deployment health check for installation status and repair actions.\n";
        }

        var obsPackage = package.Artifacts.FirstOrDefault(item =>
            item.Kind == FoundryPackageArtifactKinds.ObsPluginPackage)?.Path;
        return header + "## Install in OBS Studio\n\n" +
            $"1. Extract `{obsPackage ?? "the OBS package ZIP"}` into a disposable supported OBS installation.\n" +
            "2. Start OBS and confirm that the module log contains no load failure.\n" +
            "3. Add the generated source or filter and save the scene collection.\n" +
            "4. Restart OBS, confirm the component remains attached, then close OBS cleanly.\n\n" +
            "Native plugins execute inside OBS. Test each release in a disposable installation first.\n";
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FoundryReleaseFile> DescribeAsync(
        string kind,
        string relativePath,
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        return new(
            kind,
            relativePath,
            info.Length,
            await HashAsync(path, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool TryResolveBuildPath(
        string buildRoot,
        string relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(buildRoot)) +
            Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(
            buildRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static string ResolveReleasePath(string releaseDirectory, string relativePath)
    {
        if (!TryResolveBuildPath(releaseDirectory, relativePath, out var path))
        {
            throw new InvalidDataException($"Unsafe release path '{relativePath}'.");
        }

        return path;
    }

    private static void ResetKnownDirectory(string buildRoot, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(buildRoot)) +
            Path.DirectorySeparatorChar;
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The release directory is outside the project build root.");
        }

        if (!Directory.Exists(fullDirectory))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(fullDirectory, "*", SearchOption.AllDirectories)
            .Prepend(fullDirectory)
            .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidDataException("Foundry will not replace a release directory containing file-system links.");
        }

        Directory.Delete(fullDirectory, recursive: true);
    }

    private static string CreatePortableName(string id)
    {
        var builder = new StringBuilder(id.Length);
        foreach (var character in id.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-'
                ? character
                : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static string GetFoundryVersion() =>
        typeof(FoundryReleasePackager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.0.0";

    private static FoundryDiagnostic Error(
        string code,
        string message,
        string path,
        string fix) => new(
        code,
        FoundryDiagnosticSeverity.Error,
        message,
        new FoundryDiagnosticLocation(path),
        fix);
}
