using System.Diagnostics;
using System.Text.Json;

namespace CreatorsForge.Foundry.Build;

public sealed record VisualStudioToolchain(
    string InstallationRoot,
    string DisplayName,
    string? InstallationVersion,
    string? MsvcVersion,
    string? CompilerPath,
    string? LinkerPath,
    string? LibrarianPath,
    string? DumpbinPath,
    string? DeveloperCommandPath,
    IReadOnlyList<string> Problems)
{
    public bool IsReady => Problems.Count == 0;

    public string Summary => IsReady
        ? $"{DisplayName} — MSVC {MsvcVersion} — {CompilerPath}"
        : $"{DisplayName} — {string.Join(" ", Problems)}";
}

public static class VisualStudioToolchainService
{
    public static IReadOnlyList<VisualStudioToolchain> Discover()
    {
        var candidates = new Dictionary<string, (string? Name, string? Version)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in DiscoverWithVsWhere())
        {
            candidates[candidate.Path] = (candidate.Name, candidate.Version);
        }

        foreach (var path in DiscoverConventionalRoots())
        {
            candidates.TryAdd(path, (null, null));
        }

        return candidates
            .Select(candidate => InspectInstallation(
                candidate.Key,
                candidate.Value.Name,
                candidate.Value.Version))
            .Where(candidate => candidate.IsReady)
            .OrderByDescending(candidate => ParseVersion(candidate.InstallationVersion))
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static VisualStudioToolchain? Resolve(string? selectedInstallationRoot)
    {
        if (!string.IsNullOrWhiteSpace(selectedInstallationRoot))
        {
            return InspectInstallation(selectedInstallationRoot);
        }

        var discovered = Discover();
        return discovered.Count == 0 ? null : discovered[0];
    }

    public static VisualStudioToolchain InspectInstallation(
        string installationRoot,
        string? displayName = null,
        string? installationVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Invalid(installationRoot, displayName, installationVersion, $"Invalid installation path: {exception.Message}");
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? $"Visual Studio ({new DirectoryInfo(root).Name})"
            : displayName.Trim();
        if (!Directory.Exists(root))
        {
            return Invalid(root, name, installationVersion, "The installation folder does not exist.");
        }

        var toolsRoot = Path.Combine(root, "VC", "Tools", "MSVC");
        if (!Directory.Exists(toolsRoot))
        {
            return Invalid(root, name, installationVersion, "Desktop development with C++ is not installed.");
        }

        string? versionDirectory;
        try
        {
            versionDirectory = Directory.EnumerateDirectories(toolsRoot)
                .OrderByDescending(path => ParseVersion(Path.GetFileName(path)))
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(root, name, installationVersion, $"The MSVC tools could not be inspected: {exception.Message}");
        }

        if (versionDirectory is null)
        {
            return Invalid(root, name, installationVersion, "No MSVC tool version is installed.");
        }

        var x64 = Path.Combine(versionDirectory, "bin", "Hostx64", "x64");
        var compiler = Path.Combine(x64, "cl.exe");
        var linker = Path.Combine(x64, "link.exe");
        var librarian = Path.Combine(x64, "lib.exe");
        var dumpbin = Path.Combine(x64, "dumpbin.exe");
        var developerCommand = Path.Combine(root, "Common7", "Tools", "VsDevCmd.bat");
        var missing = new[] { compiler, linker, librarian, dumpbin, developerCommand }
            .Where(path => !File.Exists(path))
            .Select(path => $"Missing {Path.GetFileName(path)}.")
            .ToArray();

        return new(
            root,
            name,
            installationVersion,
            Path.GetFileName(versionDirectory),
            compiler,
            linker,
            librarian,
            dumpbin,
            developerCommand,
            missing);
    }

    private static (string Path, string? Name, string? Version)[] DiscoverWithVsWhere()
    {
        var installerRoot = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = Path.Combine(installerRoot, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return [];
        }

        try
        {
            var start = new ProcessStartInfo(vswhere)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
                     {
                         "-products", "*",
                         "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                         "-format", "json",
                         "-utf8",
                     })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return [];
            }
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return [];
            }
            var json = output.GetAwaiter().GetResult();
            _ = error.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return [];
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateArray()
                .Select(item => (
                    Path: item.TryGetProperty("installationPath", out var path) ? path.GetString() : null,
                    Name: item.TryGetProperty("displayName", out var name) ? name.GetString() : null,
                    Version: item.TryGetProperty("installationVersion", out var version) ? version.GetString() : null))
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => (Path.GetFullPath(item.Path!), item.Name, item.Version))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static IEnumerable<string> DiscoverConventionalRoots()
    {
        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var visualStudio = Path.Combine(programFiles, "Microsoft Visual Studio");
            if (!Directory.Exists(visualStudio))
            {
                continue;
            }

            IEnumerable<string> years;
            try
            {
                years = Directory.EnumerateDirectories(visualStudio).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var year in years)
            {
                IEnumerable<string> editions;
                try
                {
                    editions = Directory.EnumerateDirectories(year).ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var edition in editions)
                {
                    yield return Path.GetFullPath(edition);
                }
            }
        }
    }

    private static Version ParseVersion(string? value) =>
        Version.TryParse(value?.Split(['-', '+'], 2)[0], out var version)
            ? version
            : new Version(0, 0);

    private static VisualStudioToolchain Invalid(
        string root,
        string? name,
        string? version,
        string problem) =>
        new(
            root,
            string.IsNullOrWhiteSpace(name) ? "Visual Studio" : name,
            version,
            null,
            null,
            null,
            null,
            null,
            null,
            [problem]);
}
