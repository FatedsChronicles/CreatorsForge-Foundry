using System.Diagnostics;
using CreatorsForge.Foundry.Build.ObsStudio;

namespace CreatorsForge.Foundry.Build;

public sealed record CMakeToolchainStatus(
    bool IsReady,
    string? ExecutablePath,
    string? Version,
    string Details);

public sealed record WindowsSdkStatus(
    bool IsReady,
    string? Root,
    string? Version,
    string? ResourceCompilerPath,
    string? ManifestToolPath,
    string Details);

public sealed record NativeToolchainReadinessCheck(
    string Id,
    string Name,
    bool IsReady,
    string Details,
    string RecommendedAction);

public sealed record NativeToolchainReadiness(
    bool IsReady,
    CMakeToolchainStatus CMake,
    VisualStudioToolchain? VisualStudio,
    WindowsSdkStatus WindowsSdk,
    ObsSdkStatus ObsSdk,
    IReadOnlyList<NativeToolchainReadinessCheck> Checks);

public static class NativeToolchainReadinessService
{
    public static NativeToolchainReadiness Inspect(
        string? visualStudioInstallationRoot = null,
        string? cmakeExecutablePath = null,
        string? windowsKitsRoot = null,
        string? obsSdkCacheRoot = null)
    {
        var cmake = InspectCMake(cmakeExecutablePath);
        var visualStudio = VisualStudioToolchainService.Resolve(visualStudioInstallationRoot);
        var windowsSdk = InspectWindowsSdk(windowsKitsRoot);
        var obsSdk = ObsSdkManager.Inspect(obsSdkCacheRoot);
        var x64Ready = visualStudio?.IsReady == true &&
            visualStudio.CompilerPath?.Contains(
                $"{Path.DirectorySeparatorChar}Hostx64{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase) == true &&
            windowsSdk.IsReady;
        var checks = new NativeToolchainReadinessCheck[]
        {
            new("cmake", "CMake 3.20 or later", cmake.IsReady, cmake.Details,
                "Select cmake.exe or install CMake 3.20 or later."),
            new("msvc", "Visual Studio C++ x64 tools", visualStudio?.IsReady == true,
                visualStudio?.Summary ?? "Not detected.",
                "Select Visual Studio with Desktop development with C++."),
            new("windows-sdk", "Windows 10/11 SDK", windowsSdk.IsReady, windowsSdk.Details,
                "Add the Windows 10 or 11 SDK through Visual Studio Installer."),
            new("x64", "Native target architecture", x64Ready,
                x64Ready ? "Host x64 → target x64 compiler and Windows SDK libraries are ready." : "The complete Hostx64/x64 compiler and SDK surface is not ready.",
                "Install the x64 C++ tools and Windows SDK components."),
            new("obs-sdk", $"Pinned OBS SDK {obsSdk.Version}", obsSdk.IsReady,
                obsSdk.IsReady ? obsSdk.SdkRoot : obsSdk.Message ?? obsSdk.SdkRoot,
                "Install or verify the pinned OBS SDK."),
        };
        return new(checks.All(item => item.IsReady), cmake, visualStudio, windowsSdk, obsSdk, checks);
    }

    public static CMakeToolchainStatus InspectCMake(string? selectedExecutablePath = null)
    {
        var path = ResolveCMakeExecutable(selectedExecutablePath);
        if (path is null)
        {
            return new(false, null, null, "CMake was not found on PATH or in its standard installation folder.");
        }

        try
        {
            var text = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? string.Empty;
            var core = text.Split(['-', '+'], 2)[0];
            var ready = Version.TryParse(core, out var version) && version >= new Version(3, 20);
            return new(
                ready,
                path,
                string.IsNullOrWhiteSpace(text) ? null : text,
                ready
                    ? $"{text} — {path}"
                    : $"{(string.IsNullOrWhiteSpace(text) ? "Unknown version" : text)} — {path}");
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception)
        {
            return new(false, path, null, $"CMake could not be inspected: {exception.Message}");
        }
    }

    public static WindowsSdkStatus InspectWindowsSdk(string? windowsKitsRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(windowsKitsRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Windows Kits",
                "10")
            : Path.GetFullPath(windowsKitsRoot);
        if (!Directory.Exists(root))
        {
            return new(false, root, null, null, null, $"Windows SDK root not found: {root}");
        }

        var includeRoot = Path.Combine(root, "Include");
        var libRoot = Path.Combine(root, "Lib");
        var binRoot = Path.Combine(root, "bin");
        if (!Directory.Exists(includeRoot) || !Directory.Exists(libRoot) || !Directory.Exists(binRoot))
        {
            return new(false, root, null, null, null, "Windows SDK Include, Lib, or bin directories are missing.");
        }

        try
        {
            var versions = Directory.EnumerateDirectories(includeRoot)
                .Select(Path.GetFileName)
                .Where(version => Version.TryParse(version, out _))
                .OrderByDescending(ParseVersion)
                .ToArray();
            foreach (var version in versions)
            {
                var windowsHeader = Path.Combine(includeRoot, version!, "um", "Windows.h");
                var sharedHeader = Path.Combine(includeRoot, version!, "shared", "sdkddkver.h");
                var kernelLibrary = Path.Combine(libRoot, version!, "um", "x64", "kernel32.lib");
                var rc = Path.Combine(binRoot, version!, "x64", "rc.exe");
                var mt = Path.Combine(binRoot, version!, "x64", "mt.exe");
                if (new[] { windowsHeader, sharedHeader, kernelLibrary, rc, mt }.All(File.Exists))
                {
                    return new(true, root, version, rc, mt, $"Windows SDK {version} x64 — {root}");
                }
            }

            return new(false, root, null, null, null, "No complete Windows SDK x64 version contains headers, kernel32.lib, rc.exe, and mt.exe.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, root, null, null, null, $"Windows SDK could not be inspected: {exception.Message}");
        }
    }

    public static string? ResolveCMakeExecutable(string? selectedExecutablePath = null)
    {
        if (!string.IsNullOrWhiteSpace(selectedExecutablePath))
        {
            try
            {
                var selected = Path.GetFullPath(selectedExecutablePath);
                return File.Exists(selected) &&
                    string.Equals(Path.GetFileName(selected), "cmake.exe", StringComparison.OrdinalIgnoreCase)
                    ? selected
                    : null;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "cmake.exe" : "cmake"))
            .FirstOrDefault(File.Exists);
        if (fromPath is not null)
        {
            return Path.GetFullPath(fromPath);
        }

        if (OperatingSystem.IsWindows())
        {
            var standard = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "CMake",
                "bin",
                "cmake.exe");
            if (File.Exists(standard)) return standard;
        }
        return null;
    }

    private static Version ParseVersion(string? value) =>
        Version.TryParse(value, out var version) ? version : new Version(0, 0);
}
