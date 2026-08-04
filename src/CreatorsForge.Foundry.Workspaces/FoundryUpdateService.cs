using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryUpdateManifest(
    int SchemaVersion,
    string Version,
    string PackageUrl,
    string Sha256,
    long Size,
    DateTimeOffset PublishedAtUtc,
    string? ReleaseNotesUrl = null);

public sealed record FoundryUpdateCheckResult(
    bool IsSuccess,
    bool IsUpdateAvailable,
    FoundryUpdateManifest? Manifest,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public sealed record FoundryUpdateLaunchResult(
    bool IsSuccess,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public static class FoundryUpdateService
{
    private const string OfficialReleasesApiLocation =
        "https://api.github.com/repos/FatedsChronicles/CreatorsForge-Foundry/releases?per_page=100";
    private const string UpdateManifestAssetName = "foundry-update.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<FoundryUpdateCheckResult> CheckAsync(
        string? manifestLocation,
        string currentVersion,
        bool allowNetworkAccess,
        CancellationToken cancellationToken = default)
    {
        return await CheckAsync(
            manifestLocation,
            currentVersion,
            allowNetworkAccess,
            FoundryUpdateChannel.Stable,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<FoundryUpdateCheckResult> CheckAsync(
        string? manifestLocation,
        string currentVersion,
        bool allowNetworkAccess,
        FoundryUpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestLocation))
            return Failure("CFU1001", "No update manifest is configured.", "Configure a local file or HTTPS update manifest in Settings.");
        try
        {
            var effectiveManifestLocation = manifestLocation.Trim();
            if (channel == FoundryUpdateChannel.Prerelease && IsOfficialManifestLocation(effectiveManifestLocation))
            {
                if (!allowNetworkAccess)
                    return Failure("CFU1002", "Network access is disabled.", "Enable network access in Settings or use a local update manifest.");
                effectiveManifestLocation = await ResolveOfficialPrereleaseManifestLocationAsync(cancellationToken).ConfigureAwait(false)
                    ?? string.Empty;
                if (effectiveManifestLocation.Length == 0)
                    return Failure("CFU1006", "No published Foundry release with an update manifest is available on the Prerelease channel.", "Publish a non-draft GitHub Release containing foundry-update.json, then try again.");
            }

            string json;
            if (Uri.TryCreate(effectiveManifestLocation, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                if (!allowNetworkAccess)
                    return Failure("CFU1002", "Network access is disabled.", "Enable network access in Settings or use a local update manifest.");
                if (uri.Scheme != Uri.UriSchemeHttps)
                    return Failure("CFU1003", "Update manifests must use HTTPS.", "Use an HTTPS endpoint.");
                json = await Client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                json = await File.ReadAllTextAsync(Path.GetFullPath(manifestLocation), cancellationToken).ConfigureAwait(false);
            }

            var manifest = JsonSerializer.Deserialize<FoundryUpdateManifest>(json, JsonOptions);
            if (manifest is not null && !Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out _) && !Path.IsPathRooted(manifest.PackageUrl))
            {
                manifest = manifest with
                {
                    PackageUrl = Uri.TryCreate(effectiveManifestLocation, UriKind.Absolute, out var manifestUri) && manifestUri.Scheme is "http" or "https"
                        ? new Uri(manifestUri, manifest.PackageUrl).ToString()
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(effectiveManifestLocation))!, manifest.PackageUrl)),
                };
            }
            var diagnostics = Validate(manifest, effectiveManifestLocation);
            if (manifest is null || diagnostics.Any(item => item.IsError))
                return new(false, false, manifest, diagnostics);
            var available = TryCompareVersions(manifest.Version, currentVersion, out var comparison) && comparison > 0;
            return new(true, available, manifest, diagnostics);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or HttpRequestException)
        {
            return Failure("CFU1004", $"The update manifest could not be read: {exception.Message}", "Check the location and try again.");
        }
    }

    public static string? SelectOfficialPrereleaseManifestLocation(string releasesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releasesJson);
        using var document = JsonDocument.Parse(releasesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        string? selectedVersion = null;
        string? selectedLocation = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object ||
                !release.TryGetProperty("draft", out var draft) ||
                draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("prerelease", out var prerelease) ||
                prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !release.TryGetProperty("published_at", out var publishedAt) ||
                publishedAt.ValueKind != JsonValueKind.String ||
                !release.TryGetProperty("tag_name", out var tagNameElement) ||
                tagNameElement.GetString() is not { } tagName ||
                !release.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
                continue;

            var version = tagName.StartsWith('v') ? tagName[1..] : tagName;
            if (!TryParseVersion(version, out _))
                continue;

            string? location = null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name) ||
                    !string.Equals(name.GetString(), UpdateManifestAssetName, StringComparison.Ordinal) ||
                    !asset.TryGetProperty("state", out var state) ||
                    !string.Equals(state.GetString(), "uploaded", StringComparison.OrdinalIgnoreCase) ||
                    !asset.TryGetProperty("browser_download_url", out var downloadUrl) ||
                    downloadUrl.GetString() is not { } candidateLocation ||
                    !Uri.TryCreate(candidateLocation, UriKind.Absolute, out var candidateUri) ||
                    candidateUri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(candidateUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                    continue;

                location = candidateUri.ToString();
                break;
            }

            if (location is null ||
                selectedVersion is not null &&
                (!TryCompareVersions(version, selectedVersion, out var comparison) || comparison <= 0))
                continue;

            selectedVersion = version;
            selectedLocation = location;
        }

        return selectedLocation;
    }

    public static async Task<(string? PackagePath, IReadOnlyList<FoundryDiagnostic> Diagnostics)> StageAsync(
        FoundryUpdateManifest manifest,
        string destinationDirectory,
        bool allowNetworkAccess,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var extension = GetPackageExtension(manifest.PackageUrl);
            var destination = Path.Combine(
                destinationDirectory,
                $"CreatorsForge-Foundry-{manifest.Version}-Update{extension}");
            var temporary = destination + ".partial";
            if (File.Exists(temporary)) File.Delete(temporary);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                if (Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    if (!allowNetworkAccess) return (null, [Diagnostic("CFU1010", "Network access is disabled.", "Enable it explicitly or use a local package.")]);
                    if (uri.Scheme != Uri.UriSchemeHttps) return (null, [Diagnostic("CFU1011", "Update packages must use HTTPS.", "Use an HTTPS package URL.")]);
                    progress?.Report("Downloading the update package...");
                    await using var input = await Client.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    progress?.Report("Copying the local update package...");
                    await using var input = new FileStream(Path.GetFullPath(manifest.PackageUrl), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }
            var info = new FileInfo(temporary);
            string hash;
            await using (var hashInput = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(hashInput, cancellationToken).ConfigureAwait(false));
            if (info.Length != manifest.Size || !string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporary);
                return (null, [Diagnostic("CFU1012", "The update package size or SHA-256 does not match the manifest.", "Do not install this package; obtain a fresh manifest and package.")]);
            }
            File.Move(temporary, destination, overwrite: true);
            return (destination, []);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            return (null, [Diagnostic("CFU1013", $"The update package could not be staged: {exception.Message}", "Check storage and connectivity, then retry.")]);
        }
    }

    public static ProcessStartInfo CreateInstallerStartInfo(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var fullPath = Path.GetFullPath(packagePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The verified update is not a native Windows installer.", nameof(packagePath));
        return new(fullPath)
        {
            Arguments = "/CLOSEAPPLICATIONS /NORESTART",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
        };
    }

    public static FoundryUpdateLaunchResult LaunchInstaller(string packagePath)
    {
        try
        {
            if (!File.Exists(packagePath))
                return new(false, [Diagnostic("CFU1014", "The verified update installer is missing.", "Stage the update again before installing it.")]);
            Process.Start(CreateInstallerStartInfo(packagePath));
            return new(true, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            return new(false, [Diagnostic("CFU1015", $"The update installer could not be started: {exception.Message}", "Keep Foundry open, check Windows security prompts, and try again.")]);
        }
    }

    private static IReadOnlyList<FoundryDiagnostic> Validate(FoundryUpdateManifest? manifest, string location)
    {
        if (manifest is null || manifest.SchemaVersion != 1 || !TryParseVersion(manifest.Version, out _) ||
            manifest.Size < 1 || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(manifest.PackageUrl))
            return [new("CFU1005", FoundryDiagnosticSeverity.Error, "The update manifest is invalid.", new FoundryDiagnosticLocation(location), "Use the Foundry update-manifest v1 schema.")];
        return [];
    }

    private static string GetPackageExtension(string packageLocation)
    {
        var path = Uri.TryCreate(packageLocation, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsolutePath
            : packageLocation;
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            ? ".exe"
            : ".zip";
    }

    private static FoundryUpdateCheckResult Failure(string code, string message, string fix) => new(false, false, null, [Diagnostic(code, message, fix)]);
    private static FoundryDiagnostic Diagnostic(string code, string message, string fix) => new(code, FoundryDiagnosticSeverity.Error, message, SuggestedFix: fix);
    private static bool TryCompareVersions(string candidateText, string currentText, out int comparison)
    {
        comparison = 0;
        if (!TryParseVersion(candidateText, out var candidate) || !TryParseVersion(currentText, out var current)) return false;
        comparison = candidate.Core.CompareTo(current.Core);
        if (comparison != 0) return true;
        if (candidate.PreRelease.Count == 0 || current.PreRelease.Count == 0)
        {
            comparison = candidate.PreRelease.Count == current.PreRelease.Count ? 0 : candidate.PreRelease.Count == 0 ? 1 : -1;
            return true;
        }
        for (var index = 0; index < Math.Max(candidate.PreRelease.Count, current.PreRelease.Count); index++)
        {
            if (index >= candidate.PreRelease.Count) { comparison = -1; return true; }
            if (index >= current.PreRelease.Count) { comparison = 1; return true; }
            var left = candidate.PreRelease[index];
            var right = current.PreRelease[index];
            var leftNumeric = int.TryParse(left, out var leftNumber);
            var rightNumeric = int.TryParse(right, out var rightNumber);
            comparison = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric != rightNumeric
                    ? leftNumeric ? -1 : 1
                    : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0) return true;
        }
        return true;
    }

    private static bool TryParseVersion(string text, out (Version Core, IReadOnlyList<string> PreRelease) version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var withoutMetadata = text.Split('+', 2)[0];
        var parts = withoutMetadata.Split('-', 2);
        if (!Version.TryParse(parts[0], out var core) || core.Major < 0 || core.Minor < 0 || core.Build < 0) return false;
        var preRelease = parts.Length == 1 ? [] : parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && (preRelease.Length == 0 || preRelease.Any(item => !item.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')))) return false;
        version = (core, preRelease);
        return true;
    }

    private static bool IsOfficialManifestLocation(string location) =>
        string.Equals(
            location,
            FoundryUserSettings.DefaultUpdateManifestLocation,
            StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ResolveOfficialPrereleaseManifestLocationAsync(
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OfficialReleasesApiLocation);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("CreatorsForge-Foundry");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return SelectOfficialPrereleaseManifestLocation(json);
    }
}
