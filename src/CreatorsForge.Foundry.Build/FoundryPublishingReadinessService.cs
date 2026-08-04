using System.Security.Cryptography.X509Certificates;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build;

public sealed record FoundryPublishingChecklistItem(
    string Id,
    string Name,
    bool Required,
    bool Passed,
    string Details);

public sealed record FoundryPublishingReadiness(
    bool IsReady,
    IReadOnlyList<FoundryPublishingChecklistItem> Checklist,
    IReadOnlyList<FoundryReleaseDependency> Dependencies,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public static class FoundryPublishingReadinessService
{
    public static FoundryPublishingReadiness Inspect(
        FoundryProjectManifest project,
        string projectPath,
        FoundryBuildResult? build = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var root = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var publishing = project.Publishing;
        var checklist = new List<FoundryPublishingChecklistItem>();
        var diagnostics = new List<FoundryDiagnostic>();
        Add(checklist, "metadata", "Release metadata", true, publishing is not null,
            publishing is null ? "Open the publishing metadata editor." : $"{publishing.PackageName}: {publishing.Summary}");

        var licenseReady = publishing is not null && IsNonEmptyFile(root, publishing.LicenseFile);
        Add(checklist, "license", "Licence file", true, licenseReady,
            licenseReady ? publishing!.LicenseFile : "The declared licence file is missing or empty.");
        var changelogReady = publishing is not null && IsNonEmptyFile(root, publishing.ChangelogFile) &&
            File.ReadAllText(Path.Combine(root, publishing.ChangelogFile.Replace('/', Path.DirectorySeparatorChar))).Contains(project.Version, StringComparison.Ordinal);
        Add(checklist, "changelog", "Versioned changelog", true, changelogReady,
            changelogReady ? publishing!.ChangelogFile : $"The changelog must be non-empty and mention version {project.Version}.");

        var dependencies = CreateDependencyInventory(project);
        Add(checklist, "dependencies", "Dependency inventory", true, dependencies.Length > 0,
            $"{dependencies.Length} runtime, library, or tool dependencies recorded.");
        var packageReady = build?.IsSuccess == true && build.PackageIntermediate is not null &&
            HasProviderArchive(project, build.PackageIntermediate);
        Add(checklist, "package", "Provider distribution archive", true, packageReady,
            packageReady ? "The provider archive is present in the verified package IR." : "Run a successful build containing the provider package artifact.");

        var matrixPath = Path.Combine(root, "build", "test-results", "compatibility-matrix.json");
        Add(checklist, "compatibility", "Compatibility matrix reviewed", false, File.Exists(matrixPath),
            File.Exists(matrixPath) ? "A compatibility matrix result is available." : "Recommended before public publishing.");

        var signing = publishing?.Signing;
        var signingReady = signing is null || !signing.Enabled ||
            File.Exists(signing.ToolPath) && CertificateExists(signing.CertificateThumbprint!);
        Add(checklist, "signing", "Optional code signing", signing?.Enabled == true, signingReady,
            signing?.Enabled == true
                ? signingReady ? "Signing tool and certificate are available." : "Signing is enabled but the tool or certificate is unavailable."
                : "Signing is disabled.");

        foreach (var item in checklist.Where(item => item.Required && !item.Passed))
            diagnostics.Add(new("CFR2001", FoundryDiagnosticSeverity.Error, $"Publishing check failed: {item.Name}. {item.Details}", new FoundryDiagnosticLocation(projectPath)));
        return new(!checklist.Any(item => item.Required && !item.Passed), checklist, dependencies, diagnostics);
    }

    private static FoundryReleaseDependency[] CreateDependencyInventory(FoundryProjectManifest project)
    {
        var dependencies = new List<FoundryReleaseDependency>();
        if (project.Target is { } target)
            dependencies.Add(new(target.Provider == "obsstudio" ? "OBS Studio" : "Streamer.bot", target.Profile, "runtime"));
        if (project.ManagedBuild is { } managed) dependencies.Add(new(".NET Framework", managed.TargetFramework, "runtime"));
        if (project.ObsPlugin?.SdkVersion is { } sdk) dependencies.Add(new("libobs SDK", sdk, "library"));
        dependencies.AddRange((project.Publishing?.Dependencies ?? []).Select(item =>
            new FoundryReleaseDependency(item.Name, item.Version, item.Kind, item.License, item.Source)));
        dependencies.AddRange((project.Components ?? []).Select(item =>
            new FoundryReleaseDependency(item.Id, item.Version, "library", Source: "project source component")));
        return dependencies.DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsNonEmptyFile(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path) && new FileInfo(path).Length > 0;
    }
    private static bool HasProviderArchive(FoundryProjectManifest project, FoundryPackageIntermediate package) =>
        package.Artifacts.Any(item => item.Kind == (project.Target?.Provider == "obsstudio"
            ? FoundryPackageArtifactKinds.ObsPluginPackage
            : FoundryPackageArtifactKinds.StreamerBotPackage));
    private static bool CertificateExists(string thumbprint)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).Count > 0;
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }
    private static void Add(List<FoundryPublishingChecklistItem> items, string id, string name, bool required, bool passed, string details) =>
        items.Add(new(id, name, required, passed, details));
}
