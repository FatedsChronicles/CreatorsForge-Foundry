using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces.Deployment;

public enum DeploymentOperation
{
    Install,
    Update,
    Rollback,
    Uninstall,
}

public enum DeploymentFileChange
{
    Create,
    Replace,
    Restore,
    Delete,
    Unchanged,
}

public enum DeploymentHealthState
{
    NotInstalled,
    InvalidReceipt,
    MissingFiles,
    ModifiedFiles,
    UpdateAvailable,
    InstalledVersionNewer,
    RedeployRecommended,
    HostVersionChanged,
    CompletionRequired,
    LogNotObserved,
    LogFailure,
    Healthy,
}

public enum DeploymentFileHealthState
{
    Verified,
    Missing,
    Modified,
}

public sealed record DeploymentFileHealth(
    string DestinationRelativePath,
    DeploymentFileHealthState State,
    long Size,
    string ExpectedSha256,
    string? ActualSha256);

public sealed record StreamerBotDeploymentHealth(
    DeploymentHealthState State,
    string InstallationRoot,
    string ProjectId,
    string ProjectVersion,
    string? InstalledVersion,
    string? DeploymentId,
    string InstallationVersion,
    string? ReceiptInstallationVersion,
    bool? CurrentPackageMatchesReceipt,
    string? CurrentImportPackagePath,
    IReadOnlyList<DeploymentFileHealth> Files,
    StreamerBotDeploymentVerification Verification,
    string Summary,
    string RecommendedAction,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public sealed record StreamerBotInstallation(
    string RootPath,
    string ExecutablePath,
    string Version,
    string Profile);

public sealed record DeploymentFileOperation(
    DeploymentFileChange Change,
    string DestinationRelativePath,
    string? SourcePath,
    long Size,
    string Sha256,
    string? CurrentSha256);

public sealed record StreamerBotDeploymentPlan(
    DeploymentOperation Operation,
    string InstallationRoot,
    string ProjectId,
    string ProjectName,
    string ProjectVersion,
    string TargetProfile,
    string? ImportPackagePath,
    IReadOnlyList<DeploymentFileOperation> Files,
    string Fingerprint,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsReady =>
        Diagnostics.All(diagnostic => !diagnostic.IsError) &&
        Files.Count > 0;
}

public sealed record DeploymentApplyResult(
    StreamerBotDeploymentReceipt? Receipt,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess => Diagnostics.All(diagnostic => !diagnostic.IsError);
}

public sealed record StreamerBotDeploymentReceipt
{
    public int SchemaVersion { get; init; } = 1;
    public required string DeploymentId { get; init; }
    public required string ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectVersion { get; init; }
    public required string TargetProfile { get; init; }
    public required string InstallationVersion { get; init; }
    public required DateTimeOffset InstalledAtUtc { get; init; }
    public string? PreviousReceiptBackup { get; init; }
    public string? ImportPackageSha256 { get; init; }
    public StreamerBotDeploymentVerification Verification { get; init; } = new();
    public IReadOnlyList<StreamerBotDeploymentFileReceipt> Files { get; init; } = [];
}

public sealed record StreamerBotDeploymentVerification
{
    public bool PackageImported { get; init; }
    public bool CompilerReferenceAdded { get; init; }
    public bool CodeCompiled { get; init; }
    public bool RuntimeVerified { get; init; }
    public DateTimeOffset? VerifiedAtUtc { get; init; }

    public bool IsComplete =>
        PackageImported && CompilerReferenceAdded && CodeCompiled && RuntimeVerified;
}

public sealed record StreamerBotDeploymentFileReceipt(
    string DestinationRelativePath,
    string InstalledSha256,
    long Size,
    string? OriginalBackup,
    string? RollbackBackup);

public static class StreamerBotInstallationDiscovery
{
    public static IReadOnlyList<StreamerBotInstallation> Discover(
        IEnumerable<string> configuredRoots,
        string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(configuredRoots);

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in configuredRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            TryAdd(root, candidates);
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var parent = new DirectoryInfo(Path.GetFullPath(workspaceRoot));
            for (var depth = 0; parent is not null && depth < 5; depth++)
            {
                try
                {
                    foreach (var directory in parent.EnumerateDirectories("Streamer Bot*"))
                    {
                        TryAdd(directory.FullName, candidates);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }

                parent = parent.Parent;
            }
        }

        return candidates
            .Select(TryInspect)
            .Where(value => value is not null)
            .Cast<StreamerBotInstallation>()
            .OrderBy(value => value.Profile, StringComparer.Ordinal)
            .ThenBy(value => value.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static StreamerBotInstallation? TryInspect(string rootPath)
    {
        string root;
        try
        {
            root = Path.GetFullPath(rootPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var executable = Path.Combine(root, "Streamer.bot.exe");
        if (!File.Exists(executable))
        {
            return null;
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executable).FileVersion ??
                "unknown";
            return new(
                root,
                executable,
                version,
                ToCompatibilityProfile(version));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsRunning(StreamerBotInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        foreach (var process in Process.GetProcessesByName("Streamer.bot"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(
                            process.MainModule?.FileName,
                            installation.ExecutablePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                        System.ComponentModel.Win32Exception or
                        NotSupportedException)
                {
                }
            }
        }

        return false;
    }

    public static string ToCompatibilityProfile(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var prerelease = Regex.Match(
            version.Trim(),
            @"^(?<version>\d+\.\d+\.\d+)[\s-]+(?<channel>alpha|beta)\.?(?<build>\d+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        if (prerelease.Success)
        {
            var channel = prerelease.Groups["channel"].Value.ToLowerInvariant();
            var build = prerelease.Groups["build"].Value;
            return $"{prerelease.Groups["version"].Value}-{channel}" +
                (build.Length == 0 ? string.Empty : $".{build}");
        }

        return version.StartsWith("1.0.4", StringComparison.Ordinal)
            ? "1.0.4-stable"
            : version;
    }

    private static void TryAdd(string value, HashSet<string> candidates)
    {
        try
        {
            candidates.Add(Path.GetFullPath(value));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }
}

public static class StreamerBotDeploymentService
{
    private const string StateDirectoryName = ".foundry";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<StreamerBotDeploymentPlan> CreateInstallPlanAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        string installationRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var diagnostics = new List<FoundryDiagnostic>();
        var installation = ValidateInstallation(installationRoot, diagnostics);
        var root = installation?.RootPath ?? NormalizeOrEmpty(installationRoot);
        var packageIrPath = Path.Combine(projectRoot, "build", "package-ir.json");
        FoundryPackageIntermediate? package = null;

        if (!File.Exists(packageIrPath))
        {
            diagnostics.Add(Error(
                "CFD1001",
                "The project has no package IR. Build the project before deployment.",
                packageIrPath));
        }
        else
        {
            try
            {
                package = JsonSerializer.Deserialize<FoundryPackageIntermediate>(
                    await File.ReadAllTextAsync(
                        packageIrPath,
                        cancellationToken).ConfigureAwait(false),
                    JsonOptions);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                diagnostics.Add(Error(
                    "CFD1002",
                    $"The package IR could not be loaded: {exception.Message}",
                    packageIrPath));
            }
        }

        if (package is not null &&
            (!string.Equals(package.Project.Id, manifest.Id, StringComparison.Ordinal) ||
             !string.Equals(package.Project.Version, manifest.Version, StringComparison.Ordinal) ||
             !string.Equals(package.Target.Provider, "streamerbot", StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                "CFD1003",
                "The package IR does not match the open project identity and version.",
                packageIrPath));
        }

        var files = new List<DeploymentFileOperation>();
        string? importPackagePath = null;
        if (package is not null && installation is not null)
        {
            foreach (var artifact in package.Artifacts)
            {
                var sourcePath = ResolveBuildArtifact(
                    projectRoot,
                    artifact.Path,
                    diagnostics);
                if (sourcePath is null)
                {
                    continue;
                }

                if (!await VerifyArtifactAsync(
                        sourcePath,
                        artifact,
                        diagnostics,
                        cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (artifact.Kind == FoundryPackageArtifactKinds.StreamerBotPackage)
                {
                    importPackagePath = sourcePath;
                }
                else if (artifact.Kind == FoundryPackageArtifactKinds.ManagedAssembly)
                {
                    var destinationRelativePath = Path.GetFileName(sourcePath);
                    var destinationPath = Path.Combine(root, destinationRelativePath);
                    var owner = await FindOtherOwnerAsync(
                        root,
                        manifest.Id,
                        destinationRelativePath,
                        cancellationToken).ConfigureAwait(false);
                    if (owner is not null)
                    {
                        diagnostics.Add(Error(
                            "CFD1010",
                            $"Destination '{destinationRelativePath}' is owned by deployment receipt '{owner}'.",
                            destinationPath));
                        continue;
                    }

                    if (File.Exists(destinationPath) &&
                        (File.GetAttributes(destinationPath) &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add(Error(
                            "CFD1011",
                            $"Destination '{destinationRelativePath}' is a file-system link. Foundry will not replace it.",
                            destinationPath));
                        continue;
                    }

                    var currentHash = File.Exists(destinationPath)
                        ? await HashFileAsync(destinationPath, cancellationToken)
                            .ConfigureAwait(false)
                        : null;
                    var change = currentHash is not null
                        ? string.Equals(
                            currentHash,
                            artifact.Sha256,
                            StringComparison.Ordinal)
                            ? DeploymentFileChange.Unchanged
                            : DeploymentFileChange.Replace
                        : DeploymentFileChange.Create;
                    files.Add(new(
                        change,
                        destinationRelativePath,
                        sourcePath,
                        artifact.Size,
                        artifact.Sha256,
                        currentHash));
                }
            }
        }

        if (importPackagePath is null && package is not null)
        {
            diagnostics.Add(Error(
                "CFD1004",
                "The package IR does not contain a Streamer.bot import artifact.",
                packageIrPath));
        }

        if (files.Count == 0 && package is not null)
        {
            diagnostics.Add(Error(
                "CFD1005",
                "The package IR does not contain a managed assembly to deploy.",
                packageIrPath));
        }

        var receipt = installation is null
            ? null
            : await ReadReceiptAsync(root, manifest.Id, cancellationToken)
                .ConfigureAwait(false);
        if (installation is not null &&
            File.Exists(GetReceiptPath(root, manifest.Id)) &&
            receipt is null)
        {
            diagnostics.Add(Error(
                "CFD1009",
                "The existing deployment receipt is invalid. Foundry will not replace it automatically.",
                GetReceiptPath(root, manifest.Id)));
        }

        var operation = receipt is null
            ? DeploymentOperation.Install
            : DeploymentOperation.Update;
        return CreatePlan(
            operation,
            root,
            manifest,
            importPackagePath,
            files,
            diagnostics);
    }

    public static async Task<StreamerBotDeploymentPlan> CreateRollbackPlanAsync(
        FoundryProjectManifest manifest,
        string installationRoot,
        CancellationToken cancellationToken = default) =>
        await CreateReceiptPlanAsync(
            DeploymentOperation.Rollback,
            manifest,
            installationRoot,
            useOriginalBackup: false,
            cancellationToken).ConfigureAwait(false);

    public static async Task<StreamerBotDeploymentPlan> CreateUninstallPlanAsync(
        FoundryProjectManifest manifest,
        string installationRoot,
        CancellationToken cancellationToken = default) =>
        await CreateReceiptPlanAsync(
            DeploymentOperation.Uninstall,
            manifest,
            installationRoot,
            useOriginalBackup: true,
            cancellationToken).ConfigureAwait(false);

    public static async Task<StreamerBotDeploymentHealth> InspectHealthAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        string installationRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var diagnostics = new List<FoundryDiagnostic>();
        var installation = StreamerBotInstallationDiscovery.TryInspect(
            installationRoot);
        if (installation is null)
        {
            diagnostics.Add(Error(
                "CFD3001",
                "The selected directory is not a Streamer.bot installation.",
                NormalizeOrEmpty(installationRoot)));
            return CreateHealth(
                DeploymentHealthState.NotInstalled,
                NormalizeOrEmpty(installationRoot),
                manifest,
                "unknown",
                null,
                [],
                new(),
                null,
                "Installation unavailable",
                "Select a directory containing Streamer.bot.exe.",
                diagnostics);
        }

        var receiptPath = GetReceiptPath(installation.RootPath, manifest.Id);
        if (!File.Exists(receiptPath))
        {
            return CreateHealth(
                DeploymentHealthState.NotInstalled,
                installation.RootPath,
                manifest,
                installation.Version,
                null,
                [],
                new(),
                null,
                "Not installed",
                "Preview Install / Update to deploy this project.",
                diagnostics);
        }

        var receipt = await ReadReceiptAsync(
            installation.RootPath,
            manifest.Id,
            cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            diagnostics.Add(Error(
                "CFD3002",
                "The deployment receipt is invalid or uses an unsupported schema.",
                receiptPath));
            return CreateHealth(
                DeploymentHealthState.InvalidReceipt,
                installation.RootPath,
                manifest,
                installation.Version,
                null,
                [],
                new(),
                null,
                "Receipt requires attention",
                "Inspect or restore the receipt before redeploying.",
                diagnostics);
        }

        var files = new List<DeploymentFileHealth>();
        foreach (var file in receipt.Files)
        {
            var destination = ResolveInstallationPath(
                installation.RootPath,
                file.DestinationRelativePath);
            if (!File.Exists(destination))
            {
                files.Add(new(
                    file.DestinationRelativePath,
                    DeploymentFileHealthState.Missing,
                    file.Size,
                    file.InstalledSha256,
                    null));
                continue;
            }

            var actualHash = await HashFileAsync(destination, cancellationToken)
                .ConfigureAwait(false);
            files.Add(new(
                file.DestinationRelativePath,
                string.Equals(
                    actualHash,
                    file.InstalledSha256,
                    StringComparison.Ordinal)
                    ? DeploymentFileHealthState.Verified
                    : DeploymentFileHealthState.Modified,
                new FileInfo(destination).Length,
                file.InstalledSha256,
                actualHash));
        }

        var packageComparison = await CompareCurrentPackageAsync(
            manifest,
            projectRoot,
            receipt,
            cancellationToken).ConfigureAwait(false);
        var packageMatches = packageComparison.Matches;
        DeploymentHealthState state;
        string summary;
        string action;
        if (files.Any(file => file.State == DeploymentFileHealthState.Missing))
        {
            state = DeploymentHealthState.MissingFiles;
            summary = "Installed files are missing";
            action = "Preview Repair / Redeploy to restore the missing DLL.";
        }
        else if (files.Any(file => file.State == DeploymentFileHealthState.Modified))
        {
            state = DeploymentHealthState.ModifiedFiles;
            summary = "Installed files were modified";
            action = "Review the changed hash, then preview Repair / Redeploy or preserve the external modification.";
        }
        else if (!string.Equals(
                     manifest.Version,
                     receipt.ProjectVersion,
                     StringComparison.Ordinal))
        {
            var comparison = CompareVersions(manifest.Version, receipt.ProjectVersion);
            state = comparison >= 0
                ? DeploymentHealthState.UpdateAvailable
                : DeploymentHealthState.InstalledVersionNewer;
            summary = comparison >= 0
                ? $"Update available: {receipt.ProjectVersion} → {manifest.Version}"
                : $"Installed version {receipt.ProjectVersion} is newer than project {manifest.Version}";
            action = comparison >= 0
                ? "Build, then preview Repair / Redeploy to install the current project version."
                : "Open the newer project source or explicitly redeploy this older version.";
        }
        else if (packageMatches == false)
        {
            state = DeploymentHealthState.RedeployRecommended;
            summary = "Build package differs from the installed receipt";
            action = "Preview Repair / Redeploy to synchronize the installation and import code.";
        }
        else if (!string.Equals(
                     installation.Version,
                     receipt.InstallationVersion,
                     StringComparison.Ordinal))
        {
            state = DeploymentHealthState.HostVersionChanged;
            summary = $"Streamer.bot changed from {receipt.InstallationVersion} to {installation.Version}";
            action = "Recompile and run the imported action, then save the completion checklist to acknowledge this host version.";
        }
        else if (!receipt.Verification.IsComplete)
        {
            state = DeploymentHealthState.CompletionRequired;
            summary = "Files verified; host completion checklist remains";
            action = "Complete and save the import, compiler-reference, compile, and runtime checks.";
        }
        else
        {
            state = DeploymentHealthState.Healthy;
            summary = "Deployment healthy and runtime verified";
            action = "No repair is required.";
        }

        return CreateHealth(
            state,
            installation.RootPath,
            manifest,
            installation.Version,
            receipt,
            files,
            receipt.Verification,
            packageMatches,
            summary,
            action,
            diagnostics,
            packageComparison.Path);
    }

    public static async Task<DeploymentApplyResult> SaveVerificationAsync(
        string installationRoot,
        string projectId,
        string expectedDeploymentId,
        StreamerBotDeploymentVerification verification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var receipt = await ReadReceiptAsync(
            installationRoot,
            projectId,
            cancellationToken).ConfigureAwait(false);
        if (receipt is null ||
            !string.Equals(
                receipt.DeploymentId,
                expectedDeploymentId,
                StringComparison.Ordinal))
        {
            return new(null, [Error(
                "CFD3101",
                "The deployment receipt changed before the checklist was saved.",
                GetReceiptPath(installationRoot, projectId))]);
        }

        var normalized = verification with
        {
            VerifiedAtUtc = verification.IsComplete
                ? DateTimeOffset.UtcNow
                : null,
        };
        var installation = StreamerBotInstallationDiscovery.TryInspect(
            installationRoot);
        if (installation is null)
        {
            return new(null, [Error(
                "CFD3103",
                "The Streamer.bot installation could not be inspected while saving verification.",
                installationRoot)]);
        }

        var updated = receipt with
        {
            Verification = normalized,
            InstallationVersion = installation.Version,
        };
        try
        {
            await WriteJsonAsync(
                GetReceiptPath(installationRoot, projectId),
                updated,
                cancellationToken).ConfigureAwait(false);
            return new(updated, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new(null, [Error(
                "CFD3102",
                $"The completion checklist could not be saved: {exception.Message}",
                GetReceiptPath(installationRoot, projectId))]);
        }
    }

    public static async Task<DeploymentApplyResult> ApplyAsync(
        StreamerBotDeploymentPlan plan,
        string confirmedFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsReady ||
            !string.Equals(
                plan.Fingerprint,
                confirmedFingerprint,
                StringComparison.Ordinal))
        {
            return new(null, [Error(
                "CFD2001",
                "Deployment was not applied because the reviewed plan was not explicitly confirmed.",
                plan.InstallationRoot)]);
        }

        if (!string.Equals(
                plan.Fingerprint,
                ComputeFingerprint(
                    plan.Operation,
                    plan.InstallationRoot,
                    plan.ProjectId,
                    plan.ProjectVersion,
                    plan.Files),
                StringComparison.Ordinal))
        {
            return new(null, [Error(
                "CFD2002",
                "The deployment plan changed after it was reviewed.",
                plan.InstallationRoot)]);
        }

        return plan.Operation switch
        {
            DeploymentOperation.Install or DeploymentOperation.Update =>
                await ApplyInstallAsync(plan, cancellationToken).ConfigureAwait(false),
            DeploymentOperation.Rollback =>
                await ApplyReceiptPlanAsync(
                    plan,
                    restorePreviousReceipt: true,
                    cancellationToken).ConfigureAwait(false),
            DeploymentOperation.Uninstall =>
                await ApplyReceiptPlanAsync(
                    plan,
                    restorePreviousReceipt: false,
                    cancellationToken).ConfigureAwait(false),
            _ => new(null, [Error(
                "CFD2001",
                "The deployment operation is unsupported.",
                plan.InstallationRoot)]),
        };
    }

    private static async Task<DeploymentApplyResult> ApplyInstallAsync(
        StreamerBotDeploymentPlan plan,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<FoundryDiagnostic>();
        ValidateInstallation(plan.InstallationRoot, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new(null, diagnostics);
        }

        foreach (var file in plan.Files.Where(item =>
                     item.Change is not DeploymentFileChange.Unchanged))
        {
            if (file.SourcePath is null ||
                !File.Exists(file.SourcePath) ||
                !string.Equals(
                    await HashFileAsync(file.SourcePath, cancellationToken)
                        .ConfigureAwait(false),
                    file.Sha256,
                    StringComparison.Ordinal))
            {
                return new(null, [Error(
                    "CFD2003",
                    $"Source artifact '{file.SourcePath}' changed after preview.",
                    file.SourcePath ?? plan.InstallationRoot)]);
            }
        }

        foreach (var file in plan.Files)
        {
            var destination = ResolveInstallationPath(
                plan.InstallationRoot,
                file.DestinationRelativePath);
            var currentHash = File.Exists(destination)
                ? await HashFileAsync(destination, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            if (!string.Equals(
                    currentHash,
                    file.CurrentSha256,
                    StringComparison.Ordinal))
            {
                return new(null, [Error(
                    "CFD2005",
                    $"Destination '{file.DestinationRelativePath}' changed after preview.",
                    destination)]);
            }
        }

        var priorReceipt = await ReadReceiptAsync(
            plan.InstallationRoot,
            plan.ProjectId,
            cancellationToken).ConfigureAwait(false);
        var receiptExists = File.Exists(
            GetReceiptPath(plan.InstallationRoot, plan.ProjectId));
        if ((plan.Operation == DeploymentOperation.Update && priorReceipt is null) ||
            (plan.Operation == DeploymentOperation.Install && receiptExists))
        {
            return new(null, [Error(
                "CFD2006",
                "Deployment receipt state changed after preview.",
                GetReceiptPath(plan.InstallationRoot, plan.ProjectId))]);
        }

        var deploymentId = Guid.NewGuid().ToString("N");
        var backupRoot = Path.Combine(
            plan.InstallationRoot,
            StateDirectoryName,
            "backups",
            plan.ProjectId,
            deploymentId);
        var receiptPath = GetReceiptPath(plan.InstallationRoot, plan.ProjectId);
        var previousReceiptBackup = priorReceipt is null
            ? null
            : ToStateRelativePath(
                plan.InstallationRoot,
                Path.Combine(backupRoot, "previous-receipt.json"));
        var receipts = new List<StreamerBotDeploymentFileReceipt>();
        var applied = new List<(string Destination, string? RollbackBackup)>();

        try
        {
            Directory.CreateDirectory(backupRoot);
            if (priorReceipt is not null)
            {
                await WriteJsonAsync(
                    Path.Combine(backupRoot, "previous-receipt.json"),
                    priorReceipt,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var file in plan.Files)
            {
                var destination = ResolveInstallationPath(
                    plan.InstallationRoot,
                    file.DestinationRelativePath);
                var priorFile = priorReceipt?.Files.FirstOrDefault(item =>
                    string.Equals(
                        item.DestinationRelativePath,
                        file.DestinationRelativePath,
                        StringComparison.OrdinalIgnoreCase));
                var originalBackup = priorFile?.OriginalBackup;
                string? rollbackBackup = null;

                if (file.Change is not DeploymentFileChange.Unchanged)
                {
                    if (File.Exists(destination))
                    {
                        var rollbackPath = Path.Combine(
                            backupRoot,
                            "rollback",
                            file.DestinationRelativePath);
                        CopyFileCreatingDirectory(destination, rollbackPath);
                        rollbackBackup = ToStateRelativePath(
                            plan.InstallationRoot,
                            rollbackPath);
                        if (originalBackup is null && priorReceipt is null)
                        {
                            var originalPath = Path.Combine(
                                backupRoot,
                                "original",
                                file.DestinationRelativePath);
                            CopyFileCreatingDirectory(destination, originalPath);
                            originalBackup = ToStateRelativePath(
                                plan.InstallationRoot,
                                originalPath);
                        }
                    }

                    CopyFileAtomically(file.SourcePath!, destination);
                    applied.Add((destination, rollbackBackup));
                }

                receipts.Add(new(
                    file.DestinationRelativePath,
                    file.Sha256,
                    file.Size,
                    originalBackup,
                    rollbackBackup));
            }

            var installation = StreamerBotInstallationDiscovery.TryInspect(
                plan.InstallationRoot)!;
            var receipt = new StreamerBotDeploymentReceipt
            {
                DeploymentId = deploymentId,
                ProjectId = plan.ProjectId,
                ProjectName = plan.ProjectName,
                ProjectVersion = plan.ProjectVersion,
                TargetProfile = plan.TargetProfile,
                InstallationVersion = installation.Version,
                InstalledAtUtc = DateTimeOffset.UtcNow,
                PreviousReceiptBackup = previousReceiptBackup,
                ImportPackageSha256 = plan.ImportPackagePath is null
                    ? null
                    : await HashFileAsync(
                        plan.ImportPackagePath,
                        cancellationToken).ConfigureAwait(false),
                Files = receipts,
            };
            await WriteJsonAsync(receiptPath, receipt, cancellationToken)
                .ConfigureAwait(false);
            return new(receipt, diagnostics);
        }
        catch (OperationCanceledException)
        {
            RestoreAppliedFiles(plan.InstallationRoot, applied);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            RestoreAppliedFiles(plan.InstallationRoot, applied);
            diagnostics.Add(Error(
                "CFD2004",
                $"Deployment failed and applied file changes were reverted: {exception.Message}",
                plan.InstallationRoot));
            return new(null, diagnostics);
        }
    }

    private static async Task<StreamerBotDeploymentPlan> CreateReceiptPlanAsync(
        DeploymentOperation operation,
        FoundryProjectManifest manifest,
        string installationRoot,
        bool useOriginalBackup,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<FoundryDiagnostic>();
        var installation = ValidateInstallation(installationRoot, diagnostics);
        var root = installation?.RootPath ?? NormalizeOrEmpty(installationRoot);
        var receipt = installation is null
            ? null
            : await ReadReceiptAsync(root, manifest.Id, cancellationToken)
                .ConfigureAwait(false);
        if (receipt is null)
        {
            diagnostics.Add(Error(
                "CFD1101",
                "No deployment receipt exists for this project and installation.",
                GetReceiptPath(root, manifest.Id)));
            return CreatePlan(
                operation,
                root,
                manifest,
                null,
                [],
                diagnostics);
        }

        if (operation == DeploymentOperation.Rollback &&
            receipt.PreviousReceiptBackup is null &&
            receipt.Files.All(item => item.RollbackBackup is null))
        {
            diagnostics.Add(Error(
                "CFD1102",
                "The current deployment has no previous state to roll back to.",
                GetReceiptPath(root, manifest.Id)));
        }

        StreamerBotDeploymentReceipt? previousReceipt = null;
        if (operation == DeploymentOperation.Rollback &&
            receipt.PreviousReceiptBackup is not null)
        {
            var previousReceiptPath = ResolveStatePath(
                root,
                receipt.PreviousReceiptBackup);
            try
            {
                previousReceipt = JsonSerializer.Deserialize<StreamerBotDeploymentReceipt>(
                    await File.ReadAllTextAsync(
                        previousReceiptPath,
                        cancellationToken).ConfigureAwait(false),
                    JsonOptions);
                if (previousReceipt is not { SchemaVersion: 1 } ||
                    !string.Equals(
                        previousReceipt.ProjectId,
                        receipt.ProjectId,
                        StringComparison.Ordinal))
                {
                    previousReceipt = null;
                    diagnostics.Add(Error(
                        "CFD1105",
                        "The previous deployment receipt is invalid or belongs to another project.",
                        previousReceiptPath));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    JsonException or InvalidDataException)
            {
                diagnostics.Add(Error(
                    "CFD1105",
                    $"The previous deployment receipt could not be verified: {exception.Message}",
                    previousReceiptPath));
            }
        }

        var files = new List<DeploymentFileOperation>();
        foreach (var file in receipt.Files)
        {
            var destination = ResolveInstallationPath(
                root,
                file.DestinationRelativePath);
            if (!File.Exists(destination) ||
                !string.Equals(
                    await HashFileAsync(destination, cancellationToken)
                        .ConfigureAwait(false),
                    file.InstalledSha256,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(Error(
                    "CFD1103",
                    $"Installed file '{file.DestinationRelativePath}' was modified. Foundry will not overwrite or remove it.",
                    destination));
                continue;
            }

            var backup = useOriginalBackup
                ? file.OriginalBackup
                : file.RollbackBackup;
            var previousFile = previousReceipt?.Files.FirstOrDefault(item =>
                string.Equals(
                    item.DestinationRelativePath,
                    file.DestinationRelativePath,
                    StringComparison.OrdinalIgnoreCase));
            if (backup is null)
            {
                var change = DeploymentFileChange.Delete;
                if (!useOriginalBackup && previousFile is not null)
                {
                    if (!string.Equals(
                        file.InstalledSha256,
                        previousFile.InstalledSha256,
                        StringComparison.Ordinal))
                    {
                        diagnostics.Add(Error(
                            "CFD1105",
                            $"Rollback state for '{file.DestinationRelativePath}' does not match the previous receipt.",
                            destination));
                        continue;
                    }

                    change = DeploymentFileChange.Unchanged;
                }

                files.Add(new(
                    change,
                    file.DestinationRelativePath,
                    null,
                    0,
                    string.Empty,
                    file.InstalledSha256));
                continue;
            }

            var backupPath = ResolveStatePath(root, backup);
            if (!File.Exists(backupPath))
            {
                diagnostics.Add(Error(
                    "CFD1104",
                    $"Required backup '{backup}' is missing.",
                    backupPath));
                continue;
            }

            var hash = await HashFileAsync(backupPath, cancellationToken)
                .ConfigureAwait(false);
            if (!useOriginalBackup && previousFile is not null &&
                !string.Equals(
                    hash,
                    previousFile.InstalledSha256,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(Error(
                    "CFD1105",
                    $"Rollback backup for '{file.DestinationRelativePath}' does not match the previous receipt. Repair or redeploy instead of restoring it.",
                    backupPath));
                continue;
            }

            files.Add(new(
                DeploymentFileChange.Restore,
                file.DestinationRelativePath,
                backupPath,
                new FileInfo(backupPath).Length,
                hash,
                file.InstalledSha256));
        }

        return CreatePlan(
            operation,
            root,
            manifest,
            null,
            files,
            diagnostics);
    }

    private static async Task<DeploymentApplyResult> ApplyReceiptPlanAsync(
        StreamerBotDeploymentPlan plan,
        bool restorePreviousReceipt,
        CancellationToken cancellationToken)
    {
        var receipt = await ReadReceiptAsync(
            plan.InstallationRoot,
            plan.ProjectId,
            cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            return new(null, [Error(
                "CFD2101",
                "The deployment receipt disappeared after preview.",
                GetReceiptPath(plan.InstallationRoot, plan.ProjectId))]);
        }

        foreach (var file in receipt.Files)
        {
            var destination = ResolveInstallationPath(
                plan.InstallationRoot,
                file.DestinationRelativePath);
            if (!File.Exists(destination) ||
                !string.Equals(
                    await HashFileAsync(destination, cancellationToken)
                        .ConfigureAwait(false),
                    file.InstalledSha256,
                    StringComparison.Ordinal))
            {
                return new(null, [Error(
                    "CFD2102",
                    $"Installed file '{file.DestinationRelativePath}' changed after preview.",
                    destination)]);
            }
        }

        try
        {
            foreach (var file in plan.Files)
            {
                if (file.Change == DeploymentFileChange.Unchanged)
                {
                    continue;
                }

                var destination = ResolveInstallationPath(
                    plan.InstallationRoot,
                    file.DestinationRelativePath);
                if (file.Change == DeploymentFileChange.Delete)
                {
                    File.Delete(destination);
                }
                else
                {
                    if (file.SourcePath is null ||
                        !string.Equals(
                            await HashFileAsync(file.SourcePath, cancellationToken)
                                .ConfigureAwait(false),
                            file.Sha256,
                            StringComparison.Ordinal))
                    {
                        return new(null, [Error(
                            "CFD2103",
                            "A deployment backup changed after preview.",
                            file.SourcePath ?? plan.InstallationRoot)]);
                    }

                    CopyFileAtomically(file.SourcePath, destination);
                }
            }

            var receiptPath = GetReceiptPath(plan.InstallationRoot, plan.ProjectId);
            if (restorePreviousReceipt && receipt.PreviousReceiptBackup is not null)
            {
                var previousPath = ResolveStatePath(
                    plan.InstallationRoot,
                    receipt.PreviousReceiptBackup);
                var previous = JsonSerializer.Deserialize<StreamerBotDeploymentReceipt>(
                    await File.ReadAllTextAsync(
                        previousPath,
                        cancellationToken).ConfigureAwait(false),
                    JsonOptions) ?? throw new InvalidDataException(
                        "The previous deployment receipt is empty.");
                await WriteJsonAsync(receiptPath, previous, cancellationToken)
                    .ConfigureAwait(false);
                return new(previous, []);
            }

            File.Delete(receiptPath);
            return new(null, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(null, [Error(
                "CFD2104",
                $"The deployment operation failed: {exception.Message}",
                plan.InstallationRoot)]);
        }
    }

    private static StreamerBotDeploymentPlan CreatePlan(
        DeploymentOperation operation,
        string installationRoot,
        FoundryProjectManifest manifest,
        string? importPackagePath,
        IReadOnlyList<DeploymentFileOperation> files,
        IReadOnlyList<FoundryDiagnostic> diagnostics) =>
        new(
            operation,
            installationRoot,
            manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.Target?.Profile ?? string.Empty,
            importPackagePath,
            files,
            ComputeFingerprint(
                operation,
                installationRoot,
                manifest.Id,
                manifest.Version,
                files),
            diagnostics);

    private static StreamerBotDeploymentHealth CreateHealth(
        DeploymentHealthState state,
        string installationRoot,
        FoundryProjectManifest manifest,
        string installationVersion,
        StreamerBotDeploymentReceipt? receipt,
        IReadOnlyList<DeploymentFileHealth> files,
        StreamerBotDeploymentVerification verification,
        bool? packageMatches,
        string summary,
        string recommendedAction,
        IReadOnlyList<FoundryDiagnostic> diagnostics,
        string? currentImportPackagePath = null) =>
        new(
            state,
            installationRoot,
            manifest.Id,
            manifest.Version,
            receipt?.ProjectVersion,
            receipt?.DeploymentId,
            installationVersion,
            receipt?.InstallationVersion,
            packageMatches,
            currentImportPackagePath,
            files,
            verification,
            summary,
            recommendedAction,
            diagnostics);

    private static async Task<(bool? Matches, string? Path)> CompareCurrentPackageAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        StreamerBotDeploymentReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.ImportPackageSha256 is null)
        {
            return (null, null);
        }

        var packageIrPath = Path.Combine(projectRoot, "build", "package-ir.json");
        if (!File.Exists(packageIrPath))
        {
            return (null, null);
        }

        try
        {
            var package = JsonSerializer.Deserialize<FoundryPackageIntermediate>(
                await File.ReadAllTextAsync(packageIrPath, cancellationToken)
                    .ConfigureAwait(false),
                JsonOptions);
            if (package is null ||
                !string.Equals(package.Project.Id, manifest.Id, StringComparison.Ordinal) ||
                !string.Equals(
                    package.Project.Version,
                    manifest.Version,
                    StringComparison.Ordinal))
            {
                return (null, null);
            }

            var artifact = package.Artifacts.FirstOrDefault(item =>
                item.Kind == FoundryPackageArtifactKinds.StreamerBotPackage);
            if (artifact is null)
            {
                return (null, null);
            }

            var path = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    "build",
                    artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(path))
            {
                return (null, null);
            }

            var actualHash = await HashFileAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var matches = string.Equals(
                actualHash,
                receipt.ImportPackageSha256,
                StringComparison.Ordinal) &&
                string.Equals(
                    artifact.Sha256,
                    receipt.ImportPackageSha256,
                    StringComparison.Ordinal);
            return (matches, path);
        }
        catch (JsonException)
        {
            return (null, null);
        }
        catch (IOException)
        {
            return (null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static int CompareVersions(string left, string right)
    {
        static Version ParseCore(string value)
        {
            var end = value.IndexOfAny(['-', '+']);
            var core = end < 0 ? value : value[..end];
            return Version.TryParse(core, out var version)
                ? version
                : new Version(0, 0);
        }

        return ParseCore(left).CompareTo(ParseCore(right));
    }

    private static StreamerBotInstallation? ValidateInstallation(
        string installationRoot,
        List<FoundryDiagnostic> diagnostics)
    {
        var installation = StreamerBotInstallationDiscovery.TryInspect(
            installationRoot);
        if (installation is null)
        {
            diagnostics.Add(Error(
                "CFD1000",
                "The selected directory is not a Streamer.bot installation containing Streamer.bot.exe.",
                NormalizeOrEmpty(installationRoot)));
        }
        else if ((File.GetAttributes(installation.RootPath) &
                  FileAttributes.ReparsePoint) != 0)
        {
            diagnostics.Add(Error(
                "CFD1011",
                "The selected installation root is a file-system link. Choose its physical directory.",
                installation.RootPath));
        }
        else if (Directory.Exists(
                     Path.Combine(installation.RootPath, StateDirectoryName)) &&
                 (File.GetAttributes(
                      Path.Combine(installation.RootPath, StateDirectoryName)) &
                  FileAttributes.ReparsePoint) != 0)
        {
            diagnostics.Add(Error(
                "CFD1011",
                "Foundry deployment state is a file-system link and cannot be used safely.",
                Path.Combine(installation.RootPath, StateDirectoryName)));
        }
        else if (StreamerBotInstallationDiscovery.IsRunning(installation))
        {
            diagnostics.Add(Error(
                "CFD1008",
                "Close the selected Streamer.bot instance before applying deployment changes.",
                installation.ExecutablePath));
        }

        return installation;
    }

    private static string? ResolveBuildArtifact(
        string projectRoot,
        string relativePath,
        List<FoundryDiagnostic> diagnostics)
    {
        var buildRoot = Path.GetFullPath(Path.Combine(projectRoot, "build"));
        var fullPath = Path.GetFullPath(
            Path.Combine(
                buildRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsBeneath(buildRoot, fullPath) || !File.Exists(fullPath))
        {
            diagnostics.Add(Error(
                "CFD1006",
                $"Package artifact '{relativePath}' is missing or outside the build directory.",
                fullPath));
            return null;
        }

        return fullPath;
    }

    private static async Task<bool> VerifyArtifactAsync(
        string path,
        FoundryPackageArtifact artifact,
        List<FoundryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        var hash = await HashFileAsync(path, cancellationToken).ConfigureAwait(false);
        if (info.Length == artifact.Size &&
            string.Equals(hash, artifact.Sha256, StringComparison.Ordinal))
        {
            return true;
        }

        diagnostics.Add(Error(
            "CFD1007",
            $"Package artifact '{artifact.Path}' no longer matches its recorded size and SHA-256.",
            path));
        return false;
    }

    private static async Task<StreamerBotDeploymentReceipt?> ReadReceiptAsync(
        string root,
        string projectId,
        CancellationToken cancellationToken)
    {
        var path = GetReceiptPath(root, projectId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StreamerBotDeploymentReceipt>(
                await File.ReadAllTextAsync(path, cancellationToken)
                    .ConfigureAwait(false),
                JsonOptions) is { SchemaVersion: 1 } receipt
                ? receipt
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> FindOtherOwnerAsync(
        string root,
        string projectId,
        string destinationRelativePath,
        CancellationToken cancellationToken)
    {
        var receiptsRoot = Path.Combine(root, StateDirectoryName, "receipts");
        if (!Directory.Exists(receiptsRoot))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(receiptsRoot, "*.json"))
        {
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    projectId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var receipt = JsonSerializer.Deserialize<StreamerBotDeploymentReceipt>(
                    await File.ReadAllTextAsync(path, cancellationToken)
                        .ConfigureAwait(false),
                    JsonOptions);
                if (receipt is not { SchemaVersion: 1 })
                {
                    return Path.GetFileName(path) + " (invalid)";
                }

                if (receipt.Files.Any(file => string.Equals(
                        file.DestinationRelativePath,
                        destinationRelativePath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return receipt.ProjectId;
                }
            }
            catch (JsonException)
            {
                return Path.GetFileName(path) + " (invalid)";
            }
        }

        return null;
    }

    private static string GetReceiptPath(string root, string projectId) =>
        Path.Combine(root, StateDirectoryName, "receipts", $"{projectId}.json");

    private static string ResolveInstallationPath(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsBeneath(root, fullPath))
        {
            throw new InvalidDataException(
                "A deployment file path escaped the installation directory.");
        }

        return fullPath;
    }

    private static string ResolveStatePath(string root, string relativePath)
    {
        var stateRoot = Path.Combine(root, StateDirectoryName);
        var fullPath = Path.GetFullPath(
            Path.Combine(
                stateRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsBeneath(stateRoot, fullPath))
        {
            throw new InvalidDataException(
                "A deployment backup path escaped Foundry state storage.");
        }

        return fullPath;
    }

    private static string ToStateRelativePath(string root, string fullPath) =>
        Path.GetRelativePath(
                Path.Combine(root, StateDirectoryName),
                fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsBeneath(string root, string path)
    {
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(
            rootWithSeparator,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeFingerprint(
        DeploymentOperation operation,
        string root,
        string projectId,
        string version,
        IEnumerable<DeploymentFileOperation> files)
    {
        var input = new StringBuilder()
            .Append(operation).Append('\n')
            .Append(Path.GetFullPath(root)).Append('\n')
            .Append(projectId).Append('\n')
            .Append(version).Append('\n');
        foreach (var file in files)
        {
            input.Append(file.Change).Append('|')
                .Append(file.DestinationRelativePath).Append('|')
                .Append(file.Size).Append('|')
                .Append(file.Sha256).Append('|')
                .Append(file.CurrentSha256).Append('\n');
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    private static async Task<string> HashFileAsync(
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
            await SHA256.HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false));
    }

    private static void CopyFileCreatingDirectory(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyFileAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions) + "\n";
        await AtomicFile.WriteTextAsync(path, json, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void RestoreAppliedFiles(
        string root,
        IEnumerable<(string Destination, string? RollbackBackup)> files)
    {
        foreach (var (destination, rollbackBackup) in files.Reverse())
        {
            try
            {
                if (rollbackBackup is null)
                {
                    File.Delete(destination);
                }
                else
                {
                    File.Copy(
                        ResolveStatePath(root, rollbackBackup),
                        destination,
                        overwrite: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string NormalizeOrEmpty(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static FoundryDiagnostic Error(
        string code,
        string message,
        string path) =>
        new(
            code,
            FoundryDiagnosticSeverity.Error,
            message,
            new FoundryDiagnosticLocation(path));
}
