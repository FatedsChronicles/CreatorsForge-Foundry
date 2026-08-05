using CreatorsForge.Foundry.Build.ObsStudio;

namespace CreatorsForge.Foundry.Build;

public sealed record FoundryProductCheck(
    string Id,
    string Name,
    bool Required,
    bool IsReady,
    string Details,
    string Action);

public sealed record FoundryProductHealth(
    bool IsReady,
    bool NativeToolchainReady,
    IReadOnlyList<FoundryProductCheck> Checks);

public static class FoundryProductHealthService
{
    public static FoundryProductHealth Inspect(
        string? applicationDataRoot = null,
        string? visualStudioInstallationRoot = null)
    {
        var stateRoot = Path.GetFullPath(applicationDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Creators Forge", "Foundry"));
        var checks = new List<FoundryProductCheck>();
        Add(checks, "windows", "Supported Windows", true, OperatingSystem.IsWindowsVersionAtLeast(10),
            Environment.OSVersion.VersionString, "Use Windows 10 or later.");
        Add(checks, "dotnet", ".NET 10 desktop runtime", true, Environment.Version.Major >= 10,
            $"Runtime {Environment.Version}", "Install the current .NET 10 desktop runtime.");
        var writable = IsWritable(stateRoot);
        Add(checks, "storage", "Local application storage", true, writable, stateRoot,
            "Allow Foundry to write to local application data.");
        var cmake = FindCmake();
        Add(checks, "cmake", "CMake 3.20 or later", false, cmake.IsReady, cmake.Details,
            "Install CMake 3.20 or later for OBS projects.");
        var msvc = VisualStudioToolchainService.Resolve(visualStudioInstallationRoot);
        Add(checks, "msvc", "Visual Studio C++ x64 tools", false, msvc?.IsReady == true,
            msvc?.Summary ?? "Not detected.",
            "Select a Visual Studio installation with Desktop development with C++ in Development Toolchain.");
        var sdk = ObsSdkManager.Inspect();
        Add(checks, "obs-sdk", $"Pinned OBS SDK {sdk.Version}", false, sdk.IsReady,
            sdk.IsReady ? sdk.SdkRoot : sdk.Message ?? sdk.SdkRoot, "Use the Development Toolchain manager to install or verify it.");
        return new(checks.Where(item => item.Required).All(item => item.IsReady),
            checks.Where(item => item.Id is "cmake" or "msvc" or "obs-sdk").All(item => item.IsReady), checks);
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? FindExecutable(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    private static (bool IsReady, string Details) FindCmake()
    {
        var path = FindExecutable("cmake.exe") ?? FindExecutable("cmake");
        if (path is null) return (false, "Not found on PATH.");
        try
        {
            var text = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion ?? string.Empty;
            var core = text.Split(['-', '+'], 2)[0];
            var ready = Version.TryParse(core, out var version) && version >= new Version(3, 20);
            return (ready, string.IsNullOrWhiteSpace(text) ? path : $"{text} — {path}");
        }
        catch (System.ComponentModel.Win32Exception) { return (false, path); }
    }

    private static void Add(List<FoundryProductCheck> checks, string id, string name, bool required, bool ready, string details, string action) =>
        checks.Add(new(id, name, required, ready, details, action));
}
