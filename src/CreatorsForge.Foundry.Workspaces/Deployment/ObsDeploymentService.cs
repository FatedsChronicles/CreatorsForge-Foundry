using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces.Deployment;

public enum ObsLogHealthState
{
    NotAvailable,
    NotObserved,
    Healthy,
    Failure,
}

public sealed record ObsInstallation(
    string RootPath,
    string ExecutablePath,
    string Version,
    string Profile,
    string LogDirectory);

public sealed record ObsLogInspection(
    ObsLogHealthState State,
    string? LogPath,
    DateTimeOffset? LogLastWriteUtc,
    bool ModuleObserved,
    IReadOnlyList<string> RelevantLines,
    string Summary);

public sealed record ObsDeploymentPlan(
    DeploymentOperation Operation,
    string InstallationRoot,
    string ProjectId,
    string ProjectName,
    string ProjectVersion,
    string TargetProfile,
    string ModuleName,
    string? PackagePath,
    string? PackageSha256,
    IReadOnlyList<DeploymentFileOperation> Files,
    string Fingerprint,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsReady =>
        Diagnostics.All(item => !item.IsError) && Files.Count > 0;
}

public sealed record ObsDeploymentHealth(
    DeploymentHealthState State,
    string InstallationRoot,
    string ProjectId,
    string ProjectVersion,
    string? InstalledVersion,
    string? DeploymentId,
    string InstallationVersion,
    string? ReceiptInstallationVersion,
    bool? CurrentPackageMatchesReceipt,
    IReadOnlyList<DeploymentFileHealth> Files,
    ObsLogInspection Log,
    string Summary,
    string RecommendedAction,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public sealed record ObsDeploymentReceipt
{
    public int SchemaVersion { get; init; } = 1;
    public required string DeploymentId { get; init; }
    public required string ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectVersion { get; init; }
    public required string TargetProfile { get; init; }
    public required string ModuleName { get; init; }
    public required string InstallationVersion { get; init; }
    public required DateTimeOffset InstalledAtUtc { get; init; }
    public string? PreviousReceiptBackup { get; init; }
    public required string PackageSha256 { get; init; }
    public IReadOnlyList<ObsDeploymentFileReceipt> Files { get; init; } = [];
}

public sealed record ObsDeploymentFileReceipt(
    string DestinationRelativePath,
    bool IsInstalled,
    string? InstalledSha256,
    long Size,
    string? OriginalBackup,
    string? RollbackBackup,
    bool ChangedDuringDeployment);

public sealed record ObsDeploymentApplyResult(
    ObsDeploymentReceipt? Receipt,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess => Diagnostics.All(item => !item.IsError);
}

public static class ObsInstallationDiscovery
{
    public static IReadOnlyList<ObsInstallation> Discover(
        IEnumerable<string> configuredRoots,
        string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(configuredRoots);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in configuredRoots.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            TryAdd(root, candidates);
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            TryAdd(Path.Combine(programFiles, "obs-studio"), candidates);
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var parent = new DirectoryInfo(Path.GetFullPath(workspaceRoot));
            for (var depth = 0; parent is not null && depth < 5; depth++, parent = parent.Parent)
            {
                try
                {
                    foreach (var directory in parent.EnumerateDirectories())
                    {
                        if (directory.Name.Contains("OBS", StringComparison.OrdinalIgnoreCase))
                        {
                            TryAdd(directory.FullName, candidates);
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return candidates
            .Select(TryInspect)
            .Where(item => item is not null)
            .Cast<ObsInstallation>()
            .OrderBy(item => item.Version, StringComparer.Ordinal)
            .ThenBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static ObsInstallation? TryInspect(string rootPath)
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

        var executable = Path.Combine(root, "bin", "64bit", "obs64.exe");
        if (!File.Exists(executable))
        {
            return null;
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "unknown";
            var coreVersion = version.Split(['-', '+'])[0];
            var major = Version.TryParse(coreVersion, out var parsed) ? parsed.Major : 0;
            var profile = major > 0 ? $"{major}.x-windows-x64" : "unknown-windows-x64";
            var portableLogs = Path.Combine(root, "config", "obs-studio", "logs");
            var appDataLogs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "obs-studio",
                "logs");
            return new(
                root,
                executable,
                version,
                profile,
                Directory.Exists(portableLogs) ? portableLogs : appDataLogs);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsRunning(ObsInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var inaccessibleCandidate = false;
        foreach (var process in Process.GetProcessesByName("obs64"))
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
                    inaccessibleCandidate = true;
                }
            }
        }

        // If Windows hides an obs64 process path, block mutations conservatively.
        return inaccessibleCandidate;
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

public static class ObsDeploymentService
{
    private const string StateRelativePath = ".foundry/obs";
    private const long MaximumPackageBytes = 512L * 1024 * 1024;
    private const int MaximumPackageFiles = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<ObsDeploymentPlan> CreateInstallPlanAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        string installationRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var diagnostics = new List<FoundryDiagnostic>();
        var installation = ValidateInstallation(installationRoot, diagnostics);
        var root = installation?.RootPath ?? NormalizeOrEmpty(installationRoot);
        if (!string.Equals(manifest.Target?.Provider, "obsstudio", StringComparison.Ordinal) ||
            manifest.ObsPlugin is null)
        {
            diagnostics.Add(Error(
                "CFO1001",
                "The open project is not an OBS Studio plugin project.",
                manifest.Id));
        }

        var packageIrPath = Path.Combine(projectRoot, "build", "package-ir.json");
        var package = await ReadPackageIrAsync(packageIrPath, diagnostics, cancellationToken)
            .ConfigureAwait(false);
        if (package is not null &&
            (!string.Equals(package.Project.Id, manifest.Id, StringComparison.Ordinal) ||
             !string.Equals(package.Project.Version, manifest.Version, StringComparison.Ordinal) ||
             !string.Equals(package.Target.Provider, "obsstudio", StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                "CFO1002",
                "The package IR does not match the open OBS project identity and version.",
                packageIrPath));
        }

        string? packagePath = null;
        string? packageHash = null;
        var packageArtifact = package?.Artifacts.SingleOrDefault(item =>
            item.Kind == FoundryPackageArtifactKinds.ObsPluginPackage);
        if (packageArtifact is null && package is not null)
        {
            diagnostics.Add(Error(
                "CFO1003",
                "The package IR does not contain an OBS plugin package.",
                packageIrPath));
        }
        else if (packageArtifact is not null)
        {
            packagePath = ResolveBuildArtifact(projectRoot, packageArtifact.Path, diagnostics);
            if (packagePath is not null &&
                await VerifyArtifactAsync(packagePath, packageArtifact, diagnostics, cancellationToken)
                    .ConfigureAwait(false))
            {
                packageHash = packageArtifact.Sha256;
            }
            else
            {
                packagePath = null;
            }
        }

        var receipt = installation is null
            ? null
            : await ReadReceiptAsync(root, manifest.Id, cancellationToken).ConfigureAwait(false);
        if (installation is not null && File.Exists(GetReceiptPath(root, manifest.Id)) && receipt is null)
        {
            diagnostics.Add(Error(
                "CFO1004",
                "The existing OBS deployment receipt is invalid. Foundry will not replace it automatically.",
                GetReceiptPath(root, manifest.Id)));
        }

        var files = new List<DeploymentFileOperation>();
        if (installation is not null && packagePath is not null && packageHash is not null &&
            manifest.ObsPlugin is not null)
        {
            var staged = await StagePackageAsync(
                manifest,
                projectRoot,
                packagePath,
                packageHash,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
            foreach (var item in staged)
            {
                var owner = await FindOtherOwnerAsync(
                    root,
                    manifest.Id,
                    item.RelativePath,
                    cancellationToken).ConfigureAwait(false);
                if (owner is not null)
                {
                    diagnostics.Add(Error(
                        "CFO1010",
                        $"Destination '{item.RelativePath}' is owned by OBS deployment receipt '{owner}'.",
                        ResolveInstallationPath(root, item.RelativePath)));
                    continue;
                }

                var destination = ResolveInstallationPath(root, item.RelativePath);
                if (File.Exists(destination) &&
                    (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                {
                    diagnostics.Add(Error(
                        "CFO1011",
                        $"Destination '{item.RelativePath}' is a file-system link.",
                        destination));
                    continue;
                }

                var currentHash = File.Exists(destination)
                    ? await HashFileAsync(destination, cancellationToken).ConfigureAwait(false)
                    : null;
                files.Add(new(
                    currentHash is null
                        ? DeploymentFileChange.Create
                        : string.Equals(currentHash, item.Sha256, StringComparison.Ordinal)
                            ? DeploymentFileChange.Unchanged
                            : DeploymentFileChange.Replace,
                    item.RelativePath,
                    item.SourcePath,
                    item.Size,
                    item.Sha256,
                    currentHash));
            }

            if (receipt is not null)
            {
                foreach (var prior in receipt.Files.Where(prior =>
                             !files.Any(file => string.Equals(
                                 file.DestinationRelativePath,
                                 prior.DestinationRelativePath,
                                 StringComparison.OrdinalIgnoreCase))))
                {
                    var destination = ResolveInstallationPath(root, prior.DestinationRelativePath);
                    var currentHash = File.Exists(destination)
                        ? await HashFileAsync(destination, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (prior.IsInstalled && !string.Equals(
                            currentHash,
                            prior.InstalledSha256,
                            StringComparison.Ordinal))
                    {
                        diagnostics.Add(Error(
                            "CFO1012",
                            $"Previously installed file '{prior.DestinationRelativePath}' is missing or modified.",
                            destination));
                        continue;
                    }

                    files.Add(new(
                        prior.IsInstalled ? DeploymentFileChange.Delete : DeploymentFileChange.Unchanged,
                        prior.DestinationRelativePath,
                        null,
                        0,
                        string.Empty,
                        currentHash));
                }
            }
        }

        if (files.Count == 0 && package is not null)
        {
            diagnostics.Add(Error(
                "CFO1005",
                "The OBS package contains no installable module or data files.",
                packagePath ?? packageIrPath));
        }

        var operation = receipt is null ? DeploymentOperation.Install : DeploymentOperation.Update;
        return CreatePlan(
            operation,
            root,
            manifest,
            manifest.ObsPlugin?.ModuleName ?? string.Empty,
            packagePath,
            packageHash,
            files,
            diagnostics);
    }

    public static Task<ObsDeploymentPlan> CreateRollbackPlanAsync(
        FoundryProjectManifest manifest,
        string installationRoot,
        CancellationToken cancellationToken = default) =>
        CreateReceiptPlanAsync(
            DeploymentOperation.Rollback,
            manifest,
            installationRoot,
            cancellationToken);

    public static Task<ObsDeploymentPlan> CreateUninstallPlanAsync(
        FoundryProjectManifest manifest,
        string installationRoot,
        CancellationToken cancellationToken = default) =>
        CreateReceiptPlanAsync(
            DeploymentOperation.Uninstall,
            manifest,
            installationRoot,
            cancellationToken);

    public static async Task<ObsDeploymentApplyResult> ApplyAsync(
        ObsDeploymentPlan plan,
        string confirmedFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsReady ||
            !string.Equals(plan.Fingerprint, confirmedFingerprint, StringComparison.Ordinal) ||
            !string.Equals(
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
                "CFO2001",
                "OBS deployment was not applied because the reviewed plan was not explicitly confirmed or changed.",
                plan.InstallationRoot)]);
        }

        var diagnostics = new List<FoundryDiagnostic>();
        ValidateInstallation(plan.InstallationRoot, diagnostics);
        if (diagnostics.Any(item => item.IsError))
        {
            return new(null, diagnostics);
        }

        return plan.Operation switch
        {
            DeploymentOperation.Install or DeploymentOperation.Update =>
                await ApplyInstallAsync(plan, cancellationToken).ConfigureAwait(false),
            DeploymentOperation.Rollback or DeploymentOperation.Uninstall =>
                await ApplyReceiptPlanAsync(plan, cancellationToken).ConfigureAwait(false),
            _ => new(null, [Error("CFO2001", "Unsupported OBS deployment operation.", plan.InstallationRoot)]),
        };
    }

    public static async Task<ObsDeploymentHealth> InspectHealthAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        string installationRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var diagnostics = new List<FoundryDiagnostic>();
        var installation = ObsInstallationDiscovery.TryInspect(installationRoot);
        if (installation is null)
        {
            diagnostics.Add(Error(
                "CFO3001",
                "The selected directory is not an OBS Studio installation.",
                NormalizeOrEmpty(installationRoot)));
            return CreateHealth(
                DeploymentHealthState.NotInstalled,
                NormalizeOrEmpty(installationRoot),
                manifest,
                "unknown",
                null,
                [],
                EmptyLog("OBS installation unavailable."),
                null,
                "Installation unavailable",
                "Select a directory containing bin/64bit/obs64.exe.",
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
                EmptyLog("Install the plugin before inspecting its module log."),
                null,
                "Not installed",
                "Preview Install / Update to deploy this plugin.",
                diagnostics);
        }

        var receipt = await ReadReceiptAsync(
            installation.RootPath,
            manifest.Id,
            cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            diagnostics.Add(Error("CFO3002", "The OBS deployment receipt is invalid.", receiptPath));
            return CreateHealth(
                DeploymentHealthState.InvalidReceipt,
                installation.RootPath,
                manifest,
                installation.Version,
                null,
                [],
                EmptyLog("Receipt must be repaired before log correlation."),
                null,
                "Receipt requires attention",
                "Restore or inspect the receipt before redeploying.",
                diagnostics);
        }

        var files = new List<DeploymentFileHealth>();
        foreach (var file in receipt.Files.Where(item => item.IsInstalled))
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
                    file.InstalledSha256!,
                    null));
                continue;
            }

            var hash = await HashFileAsync(destination, cancellationToken).ConfigureAwait(false);
            files.Add(new(
                file.DestinationRelativePath,
                string.Equals(hash, file.InstalledSha256, StringComparison.Ordinal)
                    ? DeploymentFileHealthState.Verified
                    : DeploymentFileHealthState.Modified,
                new FileInfo(destination).Length,
                file.InstalledSha256!,
                hash));
        }

        var packageComparison = await CompareCurrentPackageAsync(
            manifest,
            projectRoot,
            receipt,
            cancellationToken).ConfigureAwait(false);
        var packageMatches = packageComparison.Matches;
        var log = await InspectLogAsync(installation, receipt, cancellationToken).ConfigureAwait(false);
        DeploymentHealthState state;
        string summary;
        string action;
        if (files.Any(item => item.State == DeploymentFileHealthState.Missing))
        {
            state = DeploymentHealthState.MissingFiles;
            summary = "Installed OBS files are missing";
            action = "Preview Repair / Redeploy to restore them.";
        }
        else if (files.Any(item => item.State == DeploymentFileHealthState.Modified))
        {
            state = DeploymentHealthState.ModifiedFiles;
            summary = "Installed OBS files were modified";
            action = "Review the hashes before repairing or preserving the external changes.";
        }
        else if (log.State == ObsLogHealthState.Failure)
        {
            state = DeploymentHealthState.LogFailure;
            summary = "OBS reported a module load failure";
            action = "Review the relevant log lines, repair the installation, and restart OBS.";
        }
        else if (!string.Equals(manifest.Version, receipt.ProjectVersion, StringComparison.Ordinal))
        {
            var comparison = CompareVersions(manifest.Version, receipt.ProjectVersion);
            state = comparison >= 0
                ? DeploymentHealthState.UpdateAvailable
                : DeploymentHealthState.InstalledVersionNewer;
            summary = comparison >= 0
                ? $"Update available: {receipt.ProjectVersion} → {manifest.Version}"
                : $"Installed version {receipt.ProjectVersion} is newer than project {manifest.Version}";
            action = "Build and preview Install / Update after confirming the intended version.";
        }
        else if (packageMatches == false)
        {
            state = DeploymentHealthState.RedeployRecommended;
            summary = "The current build package differs from the installed receipt";
            action = "Preview Repair / Redeploy to synchronize the installation.";
        }
        else if (!string.Equals(installation.Version, receipt.InstallationVersion, StringComparison.Ordinal))
        {
            state = DeploymentHealthState.HostVersionChanged;
            summary = $"OBS changed from {receipt.InstallationVersion} to {installation.Version}";
            action = "Launch OBS and re-check the module log for this host version.";
        }
        else if (log.State is ObsLogHealthState.NotAvailable or ObsLogHealthState.NotObserved)
        {
            state = DeploymentHealthState.LogNotObserved;
            summary = "Files verified; a post-install OBS log has not confirmed the module";
            action = "Start and close OBS, then Check Health again.";
        }
        else
        {
            state = DeploymentHealthState.Healthy;
            summary = "OBS deployment healthy and module observed without a load failure";
            action = "No repair is required.";
        }

        return CreateHealth(
            state,
            installation.RootPath,
            manifest,
            installation.Version,
            receipt,
            files,
            log,
            packageMatches,
            summary,
            action,
            diagnostics);
    }

    public static async Task<ObsLogInspection> InspectLogAsync(
        ObsInstallation installation,
        ObsDeploymentReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!Directory.Exists(installation.LogDirectory))
        {
            return EmptyLog($"OBS log directory not found: {installation.LogDirectory}");
        }

        FileInfo? latest;
        try
        {
            latest = new DirectoryInfo(installation.LogDirectory)
                .EnumerateFiles("*.txt")
                .OrderByDescending(item => item.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return EmptyLog($"OBS logs could not be inspected: {exception.Message}");
        }

        if (latest is null || latest.LastWriteTimeUtc < receipt.InstalledAtUtc.UtcDateTime)
        {
            return new(
                ObsLogHealthState.NotObserved,
                latest?.FullName,
                latest?.LastWriteTimeUtc,
                false,
                [],
                "No OBS log written after this deployment was found.");
        }

        string[] lines;
        try
        {
            lines = (await File.ReadAllLinesAsync(latest.FullName, cancellationToken)
                    .ConfigureAwait(false))
                .Where(line => line.Contains(receipt.ModuleName, StringComparison.OrdinalIgnoreCase))
                .TakeLast(50)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return EmptyLog($"The latest OBS log could not be read: {exception.Message}");
        }

        var observed = lines.Length > 0;
        var failures = lines.Where(line =>
            line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("not loaded", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("could not load", StringComparison.OrdinalIgnoreCase)).ToArray();
        return failures.Length > 0
            ? new(
                ObsLogHealthState.Failure,
                latest.FullName,
                latest.LastWriteTimeUtc,
                observed,
                failures,
                "The latest OBS log contains a module-related failure.")
            : observed
                ? new(
                    ObsLogHealthState.Healthy,
                    latest.FullName,
                    latest.LastWriteTimeUtc,
                    true,
                    lines,
                    "The module appears in the latest OBS log without a load failure.")
                : new(
                    ObsLogHealthState.NotObserved,
                    latest.FullName,
                    latest.LastWriteTimeUtc,
                    false,
                    [],
                    "The latest OBS log does not mention this module.");
    }

    private static async Task<ObsDeploymentApplyResult> ApplyInstallAsync(
        ObsDeploymentPlan plan,
        CancellationToken cancellationToken)
    {
        foreach (var file in plan.Files)
        {
            var destination = ResolveInstallationPath(plan.InstallationRoot, file.DestinationRelativePath);
            var currentHash = File.Exists(destination)
                ? await HashFileAsync(destination, cancellationToken).ConfigureAwait(false)
                : null;
            if (!string.Equals(currentHash, file.CurrentSha256, StringComparison.Ordinal))
            {
                return new(null, [Error(
                    "CFO2002",
                    $"Destination '{file.DestinationRelativePath}' changed after preview.",
                    destination)]);
            }

            if (file.Change is DeploymentFileChange.Create or DeploymentFileChange.Replace or DeploymentFileChange.Restore &&
                (file.SourcePath is null || !File.Exists(file.SourcePath) ||
                 !string.Equals(
                     await HashFileAsync(file.SourcePath, cancellationToken).ConfigureAwait(false),
                     file.Sha256,
                     StringComparison.Ordinal)))
            {
                return new(null, [Error(
                    "CFO2003",
                    $"Source artifact '{file.SourcePath}' changed after preview.",
                    file.SourcePath ?? plan.InstallationRoot)]);
            }
        }

        var prior = await ReadReceiptAsync(
            plan.InstallationRoot,
            plan.ProjectId,
            cancellationToken).ConfigureAwait(false);
        var receiptPath = GetReceiptPath(plan.InstallationRoot, plan.ProjectId);
        if ((plan.Operation == DeploymentOperation.Update && prior is null) ||
            (plan.Operation == DeploymentOperation.Install && File.Exists(receiptPath)))
        {
            return new(null, [Error("CFO2004", "OBS receipt state changed after preview.", receiptPath)]);
        }

        var deploymentId = Guid.NewGuid().ToString("N");
        var backupRoot = Path.Combine(
            GetStateRoot(plan.InstallationRoot),
            "backups",
            plan.ProjectId,
            deploymentId);
        var previousReceiptBackup = prior is null
            ? null
            : ToStateRelativePath(
                plan.InstallationRoot,
                Path.Combine(backupRoot, "previous-receipt.json"));
        var applied = new List<(string Destination, string? RollbackBackup)>();
        var receipts = new List<ObsDeploymentFileReceipt>();
        try
        {
            Directory.CreateDirectory(backupRoot);
            if (prior is not null)
            {
                await WriteJsonAsync(
                    Path.Combine(backupRoot, "previous-receipt.json"),
                    prior,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var file in plan.Files)
            {
                var destination = ResolveInstallationPath(plan.InstallationRoot, file.DestinationRelativePath);
                var priorFile = prior?.Files.FirstOrDefault(item => string.Equals(
                    item.DestinationRelativePath,
                    file.DestinationRelativePath,
                    StringComparison.OrdinalIgnoreCase));
                var originalBackup = priorFile?.OriginalBackup;
                string? rollbackBackup = null;
                var changed = file.Change != DeploymentFileChange.Unchanged;
                if (!changed && priorFile is { IsInstalled: false })
                {
                    receipts.Add(priorFile with
                    {
                        RollbackBackup = null,
                        ChangedDuringDeployment = false,
                    });
                    continue;
                }

                if (changed && File.Exists(destination))
                {
                    var backup = Path.Combine(backupRoot, "rollback", file.DestinationRelativePath);
                    CopyFileCreatingDirectory(destination, backup);
                    rollbackBackup = ToStateRelativePath(plan.InstallationRoot, backup);
                    if (originalBackup is null && priorFile is null)
                    {
                        var original = Path.Combine(backupRoot, "original", file.DestinationRelativePath);
                        CopyFileCreatingDirectory(destination, original);
                        originalBackup = ToStateRelativePath(plan.InstallationRoot, original);
                    }
                }

                applied.Add((destination, rollbackBackup));
                if (file.Change == DeploymentFileChange.Delete)
                {
                    File.Delete(destination);
                    receipts.Add(new(
                        file.DestinationRelativePath,
                        false,
                        null,
                        0,
                        originalBackup,
                        rollbackBackup,
                        true));
                }
                else
                {
                    if (file.Change is DeploymentFileChange.Create or DeploymentFileChange.Replace or DeploymentFileChange.Restore)
                    {
                        CopyFileAtomically(file.SourcePath!, destination);
                    }

                    var hash = await HashFileAsync(destination, cancellationToken).ConfigureAwait(false);
                    receipts.Add(new(
                        file.DestinationRelativePath,
                        true,
                        hash,
                        new FileInfo(destination).Length,
                        originalBackup,
                        rollbackBackup,
                        changed));
                }
            }

            var installation = ObsInstallationDiscovery.TryInspect(plan.InstallationRoot)!;
            var receipt = new ObsDeploymentReceipt
            {
                DeploymentId = deploymentId,
                ProjectId = plan.ProjectId,
                ProjectName = plan.ProjectName,
                ProjectVersion = plan.ProjectVersion,
                TargetProfile = plan.TargetProfile,
                ModuleName = plan.ModuleName,
                InstallationVersion = installation.Version,
                InstalledAtUtc = DateTimeOffset.UtcNow,
                PreviousReceiptBackup = previousReceiptBackup,
                PackageSha256 = plan.PackageSha256!,
                Files = receipts,
            };
            await WriteJsonAsync(receiptPath, receipt, cancellationToken).ConfigureAwait(false);
            return new(receipt, []);
        }
        catch (OperationCanceledException)
        {
            RestoreAppliedFiles(plan.InstallationRoot, applied);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            RestoreAppliedFiles(plan.InstallationRoot, applied);
            return new(null, [Error(
                "CFO2005",
                $"OBS deployment failed and file recovery was attempted: {exception.Message}",
                plan.InstallationRoot)]);
        }
    }

    private static async Task<ObsDeploymentPlan> CreateReceiptPlanAsync(
        DeploymentOperation operation,
        FoundryProjectManifest manifest,
        string installationRoot,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<FoundryDiagnostic>();
        var installation = ValidateInstallation(installationRoot, diagnostics);
        var root = installation?.RootPath ?? NormalizeOrEmpty(installationRoot);
        var receipt = installation is null
            ? null
            : await ReadReceiptAsync(root, manifest.Id, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            diagnostics.Add(Error(
                "CFO2101",
                "No valid OBS deployment receipt exists for this project.",
                GetReceiptPath(root, manifest.Id)));
            return CreatePlan(operation, root, manifest, manifest.ObsPlugin?.ModuleName ?? string.Empty, null, null, [], diagnostics);
        }

        var files = new List<DeploymentFileOperation>();
        foreach (var file in receipt.Files)
        {
            var destination = ResolveInstallationPath(root, file.DestinationRelativePath);
            var currentHash = File.Exists(destination)
                ? await HashFileAsync(destination, cancellationToken).ConfigureAwait(false)
                : null;
            if ((file.IsInstalled && !string.Equals(currentHash, file.InstalledSha256, StringComparison.Ordinal)) ||
                (!file.IsInstalled && currentHash is not null))
            {
                diagnostics.Add(Error(
                    "CFO2102",
                    $"Owned file '{file.DestinationRelativePath}' is missing or modified; Foundry will not {operation.ToString().ToLowerInvariant()} it.",
                    destination));
                continue;
            }

            string? source = null;
            DeploymentFileChange change;
            if (operation == DeploymentOperation.Rollback && file.ChangedDuringDeployment)
            {
                source = file.RollbackBackup is null
                    ? null
                    : ResolveStatePath(root, file.RollbackBackup);
                change = source is null ? DeploymentFileChange.Delete : DeploymentFileChange.Restore;
            }
            else if (operation == DeploymentOperation.Rollback)
            {
                change = DeploymentFileChange.Unchanged;
            }
            else
            {
                source = file.OriginalBackup is null
                    ? null
                    : ResolveStatePath(root, file.OriginalBackup);
                change = source is null
                    ? file.IsInstalled ? DeploymentFileChange.Delete : DeploymentFileChange.Unchanged
                    : DeploymentFileChange.Restore;
            }

            var sourceHash = source is null
                ? string.Empty
                : await HashFileAsync(source, cancellationToken).ConfigureAwait(false);
            files.Add(new(
                change,
                file.DestinationRelativePath,
                source,
                source is null ? 0 : new FileInfo(source).Length,
                sourceHash,
                currentHash));
        }

        return CreatePlan(
            operation,
            root,
            manifest,
            receipt.ModuleName,
            null,
            receipt.PackageSha256,
            files,
            diagnostics);
    }

    private static async Task<ObsDeploymentApplyResult> ApplyReceiptPlanAsync(
        ObsDeploymentPlan plan,
        CancellationToken cancellationToken)
    {
        var receipt = await ReadReceiptAsync(
            plan.InstallationRoot,
            plan.ProjectId,
            cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            return new(null, [Error("CFO2201", "The OBS receipt disappeared after preview.", plan.InstallationRoot)]);
        }

        foreach (var file in plan.Files)
        {
            var destination = ResolveInstallationPath(plan.InstallationRoot, file.DestinationRelativePath);
            var currentHash = File.Exists(destination)
                ? await HashFileAsync(destination, cancellationToken).ConfigureAwait(false)
                : null;
            if (!string.Equals(currentHash, file.CurrentSha256, StringComparison.Ordinal))
            {
                return new(null, [Error(
                    "CFO2202",
                    $"Destination '{file.DestinationRelativePath}' changed after preview.",
                    destination)]);
            }
        }

        try
        {
            foreach (var file in plan.Files.Where(item => item.Change != DeploymentFileChange.Unchanged))
            {
                var destination = ResolveInstallationPath(plan.InstallationRoot, file.DestinationRelativePath);
                if (file.Change == DeploymentFileChange.Delete)
                {
                    File.Delete(destination);
                }
                else
                {
                    if (file.SourcePath is null ||
                        !string.Equals(
                            await HashFileAsync(file.SourcePath, cancellationToken).ConfigureAwait(false),
                            file.Sha256,
                            StringComparison.Ordinal))
                    {
                        return new(null, [Error("CFO2203", "An OBS deployment backup changed after preview.", file.SourcePath ?? plan.InstallationRoot)]);
                    }

                    CopyFileAtomically(file.SourcePath, destination);
                }
            }

            var receiptPath = GetReceiptPath(plan.InstallationRoot, plan.ProjectId);
            if (plan.Operation == DeploymentOperation.Rollback && receipt.PreviousReceiptBackup is not null)
            {
                var previousPath = ResolveStatePath(plan.InstallationRoot, receipt.PreviousReceiptBackup);
                var previous = JsonSerializer.Deserialize<ObsDeploymentReceipt>(
                    await File.ReadAllTextAsync(previousPath, cancellationToken).ConfigureAwait(false),
                    JsonOptions) ?? throw new InvalidDataException("The previous OBS receipt is empty.");
                await WriteJsonAsync(receiptPath, previous, cancellationToken).ConfigureAwait(false);
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
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new(null, [Error("CFO2204", $"OBS recovery operation failed: {exception.Message}", plan.InstallationRoot)]);
        }
    }

    private static async Task<IReadOnlyList<StagedFile>> StagePackageAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        string packagePath,
        string packageHash,
        List<FoundryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var moduleName = manifest.ObsPlugin!.ModuleName;
        var stagingRoot = Path.Combine(
            projectRoot,
            "build",
            "obj",
            "obs-deployment",
            packageHash[..16]);
        try
        {
            ResetGeneratedDirectory(Path.Combine(projectRoot, "build"), stagingRoot);
            Directory.CreateDirectory(stagingRoot);
            using var archive = ZipFile.OpenRead(packagePath);
            if (archive.Entries.Count > MaximumPackageFiles ||
                archive.Entries.Sum(item => item.Length) > MaximumPackageBytes)
            {
                throw new InvalidDataException("The OBS package exceeds the deployment extraction limits.");
            }

            var result = new List<StagedFile>();
            var metadataFound = false;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var relative = entry.FullName.Replace('\\', '/');
                if (string.Equals(relative, "foundry-package.json", StringComparison.OrdinalIgnoreCase))
                {
                    await ValidatePackageMetadataAsync(entry, manifest, cancellationToken)
                        .ConfigureAwait(false);
                    metadataFound = true;
                    continue;
                }

                var moduleBinary = $"obs-plugins/64bit/{moduleName}.dll";
                var dataPrefix = $"data/obs-plugins/{moduleName}/";
                if (!string.Equals(relative, moduleBinary, StringComparison.OrdinalIgnoreCase) &&
                    !relative.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Package entry '{relative}' is outside the module's install scope.");
                }

                if (Path.IsPathRooted(relative) || relative.Split('/').Contains("..", StringComparer.Ordinal) ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                {
                    throw new InvalidDataException($"Package entry '{relative}' is unsafe.");
                }

                var destination = ResolveInstallationPath(stagingRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var input = entry.Open())
                await using (var output = new FileStream(
                                 destination,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 useAsync: true))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                result.Add(new(
                    relative,
                    destination,
                    new FileInfo(destination).Length,
                    await HashFileAsync(destination, cancellationToken).ConfigureAwait(false)));
            }

            if (!result.Any(item => string.Equals(
                    item.RelativePath,
                    $"obs-plugins/64bit/{moduleName}.dll",
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("The OBS package does not contain its declared module DLL.");
            }

            if (!metadataFound)
            {
                throw new InvalidDataException("The OBS package does not contain foundry-package.json.");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            diagnostics.Add(Error("CFO1006", $"The OBS package could not be staged safely: {exception.Message}", packagePath));
            return [];
        }
    }

    private static async Task ValidatePackageMetadataAsync(
        ZipArchiveEntry entry,
        FoundryProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        var projectId = root.TryGetProperty("projectId", out var projectIdValue)
            ? projectIdValue.GetString()
            : null;
        var projectVersion = root.TryGetProperty("projectVersion", out var versionValue)
            ? versionValue.GetString()
            : null;
        var moduleName = root.TryGetProperty("moduleName", out var moduleValue)
            ? moduleValue.GetString()
            : null;
        if (!string.Equals(projectId, manifest.Id, StringComparison.Ordinal) ||
            !string.Equals(projectVersion, manifest.Version, StringComparison.Ordinal) ||
            !string.Equals(moduleName, manifest.ObsPlugin!.ModuleName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The OBS package metadata does not match the open project's ID, version, and module name.");
        }
    }

    private static async Task<FoundryPackageIntermediate?> ReadPackageIrAsync(
        string path,
        List<FoundryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            diagnostics.Add(Error("CFO1007", "Build the project before OBS deployment.", path));
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FoundryPackageIntermediate>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add(Error("CFO1008", $"Package IR could not be loaded: {exception.Message}", path));
            return null;
        }
    }

    private static async Task<(bool? Matches, string? Path)> CompareCurrentPackageAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        ObsDeploymentReceipt receipt,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectRoot, "build", "package-ir.json");
        if (!File.Exists(path))
        {
            return (null, null);
        }

        try
        {
            var package = JsonSerializer.Deserialize<FoundryPackageIntermediate>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                JsonOptions);
            var artifact = package?.Artifacts.SingleOrDefault(item =>
                item.Kind == FoundryPackageArtifactKinds.ObsPluginPackage);
            if (package is null || artifact is null ||
                !string.Equals(package.Project.Id, manifest.Id, StringComparison.Ordinal))
            {
                return (false, null);
            }

            var packagePath = ResolveBuildArtifact(projectRoot, artifact.Path, []);
            if (packagePath is null || !File.Exists(packagePath))
            {
                return (false, packagePath);
            }

            var hash = await HashFileAsync(packagePath, cancellationToken).ConfigureAwait(false);
            return (string.Equals(hash, receipt.PackageSha256, StringComparison.Ordinal), packagePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null);
        }
    }

    private static ObsDeploymentPlan CreatePlan(
        DeploymentOperation operation,
        string root,
        FoundryProjectManifest manifest,
        string moduleName,
        string? packagePath,
        string? packageHash,
        IReadOnlyList<DeploymentFileOperation> files,
        IReadOnlyList<FoundryDiagnostic> diagnostics) => new(
        operation,
        root,
        manifest.Id,
        manifest.Name,
        manifest.Version,
        manifest.Target?.Profile ?? string.Empty,
        moduleName,
        packagePath,
        packageHash,
        files,
        ComputeFingerprint(operation, root, manifest.Id, manifest.Version, files),
        diagnostics);

    private static ObsDeploymentHealth CreateHealth(
        DeploymentHealthState state,
        string root,
        FoundryProjectManifest manifest,
        string installationVersion,
        ObsDeploymentReceipt? receipt,
        IReadOnlyList<DeploymentFileHealth> files,
        ObsLogInspection log,
        bool? packageMatches,
        string summary,
        string action,
        IReadOnlyList<FoundryDiagnostic> diagnostics) => new(
        state,
        root,
        manifest.Id,
        manifest.Version,
        receipt?.ProjectVersion,
        receipt?.DeploymentId,
        installationVersion,
        receipt?.InstallationVersion,
        packageMatches,
        files,
        log,
        summary,
        action,
        diagnostics);

    private static ObsLogInspection EmptyLog(string summary) => new(
        ObsLogHealthState.NotAvailable,
        null,
        null,
        false,
        [],
        summary);

    private static ObsInstallation? ValidateInstallation(
        string root,
        List<FoundryDiagnostic> diagnostics)
    {
        var installation = ObsInstallationDiscovery.TryInspect(root);
        if (installation is null)
        {
            diagnostics.Add(Error("CFO1000", "Select an OBS installation containing bin/64bit/obs64.exe.", NormalizeOrEmpty(root)));
        }
        else if ((File.GetAttributes(installation.RootPath) & FileAttributes.ReparsePoint) != 0)
        {
            diagnostics.Add(Error("CFO1011", "The OBS installation root is a file-system link.", installation.RootPath));
        }
        else if (Directory.Exists(GetStateRoot(installation.RootPath)) &&
                 (File.GetAttributes(GetStateRoot(installation.RootPath)) & FileAttributes.ReparsePoint) != 0)
        {
            diagnostics.Add(Error("CFO1011", "Foundry OBS deployment state is a file-system link.", GetStateRoot(installation.RootPath)));
        }
        else if (TryParseVersion(installation.Version, out _) &&
                 !FoundryObsCompatibility.IsSupportedRuntime(installation.Version))
        {
            diagnostics.Add(Error(
                "CFO1015",
                $"OBS {installation.Version} is not an exact supported runtime ({FoundryObsCompatibility.SupportedRuntimeDisplay}).",
                installation.ExecutablePath));
        }
        else if (ObsInstallationDiscovery.IsRunning(installation))
        {
            diagnostics.Add(Error("CFO1009", "Close the selected OBS instance before previewing or applying deployment changes.", installation.ExecutablePath));
        }

        return installation;
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        var core = text.Split(['-', '+'], 2)[0];
        return Version.TryParse(core, out version!);
    }

    private static string? ResolveBuildArtifact(
        string projectRoot,
        string relativePath,
        List<FoundryDiagnostic> diagnostics)
    {
        var buildRoot = Path.GetFullPath(Path.Combine(projectRoot, "build"));
        var path = Path.GetFullPath(Path.Combine(buildRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsBeneath(buildRoot, path) || !File.Exists(path))
        {
            diagnostics.Add(Error("CFO1013", $"Build artifact '{relativePath}' is missing or outside build.", path));
            return null;
        }

        return path;
    }

    private static async Task<bool> VerifyArtifactAsync(
        string path,
        FoundryPackageArtifact artifact,
        List<FoundryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var hash = await HashFileAsync(path, cancellationToken).ConfigureAwait(false);
        if (new FileInfo(path).Length == artifact.Size &&
            string.Equals(hash, artifact.Sha256, StringComparison.Ordinal))
        {
            return true;
        }

        diagnostics.Add(Error("CFO1014", "The OBS package no longer matches package IR.", path));
        return false;
    }

    private static async Task<ObsDeploymentReceipt?> ReadReceiptAsync(
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
            return JsonSerializer.Deserialize<ObsDeploymentReceipt>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
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
        string relativePath,
        CancellationToken cancellationToken)
    {
        var receipts = Path.Combine(GetStateRoot(root), "receipts");
        if (!Directory.Exists(receipts))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(receipts, "*.json"))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(path), projectId, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var receipt = JsonSerializer.Deserialize<ObsDeploymentReceipt>(
                    await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                    JsonOptions);
                if (receipt is not { SchemaVersion: 1 })
                {
                    return Path.GetFileName(path) + " (invalid)";
                }

                if (receipt.Files.Any(item => item.IsInstalled && string.Equals(
                        item.DestinationRelativePath,
                        relativePath,
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

    private static string GetStateRoot(string root) =>
        Path.Combine(root, StateRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetReceiptPath(string root, string projectId) =>
        Path.Combine(GetStateRoot(root), "receipts", $"{projectId}.json");

    private static string ResolveInstallationPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsBeneath(root, path))
        {
            throw new InvalidDataException("An OBS deployment path escaped the installation root.");
        }

        return path;
    }

    private static string ResolveStatePath(string root, string relativePath)
    {
        var stateRoot = GetStateRoot(root);
        var path = Path.GetFullPath(Path.Combine(stateRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsBeneath(stateRoot, path))
        {
            throw new InvalidDataException("An OBS backup path escaped Foundry state.");
        }

        return path;
    }

    private static string ToStateRelativePath(string root, string path) =>
        Path.GetRelativePath(GetStateRoot(root), path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsBeneath(string root, string path) =>
        Path.GetFullPath(path).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

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

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    private static int CompareVersions(string left, string right)
    {
        static Version Parse(string value)
        {
            var end = value.IndexOfAny(['-', '+']);
            return Version.TryParse(end < 0 ? value : value[..end], out var version)
                ? version
                : new Version(0, 0);
        }

        return Parse(left).CompareTo(Parse(right));
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
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

    private static void RestoreAppliedFiles(
        string root,
        IEnumerable<(string Destination, string? RollbackBackup)> files)
    {
        foreach (var (destination, backup) in files.Reverse())
        {
            try
            {
                if (backup is null)
                {
                    File.Delete(destination);
                }
                else
                {
                    File.Copy(ResolveStatePath(root, backup), destination, overwrite: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await AtomicFile.WriteTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonOptions) + "\n",
            cancellationToken).ConfigureAwait(false);
    }

    private static void ResetGeneratedDirectory(string buildRoot, string path)
    {
        if (!IsBeneath(buildRoot, path))
        {
            throw new InvalidDataException("OBS deployment staging escaped the build root.");
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)
            .Prepend(path)
            .Any(item => (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidDataException("OBS deployment staging contains a file-system link.");
        }

        Directory.Delete(path, recursive: true);
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

    private static FoundryDiagnostic Error(string code, string message, string path) => new(
        code,
        FoundryDiagnosticSeverity.Error,
        message,
        new FoundryDiagnosticLocation(path));

    private sealed record StagedFile(
        string RelativePath,
        string SourcePath,
        long Size,
        string Sha256);
}
