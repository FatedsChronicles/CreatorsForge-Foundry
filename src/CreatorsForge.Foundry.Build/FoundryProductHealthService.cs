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
        string? visualStudioInstallationRoot = null,
        string? cmakeExecutablePath = null)
    {
        var stateRoot = Path.GetFullPath(applicationDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Creators Forge",
            "Foundry"));
        var checks = new List<FoundryProductCheck>();
        Add(checks, "windows", "Supported Windows", true, OperatingSystem.IsWindowsVersionAtLeast(10),
            Environment.OSVersion.VersionString, "Use Windows 10 or later.");
        Add(checks, "dotnet", ".NET 10 desktop runtime", true, Environment.Version.Major >= 10,
            $"Runtime {Environment.Version}", "Install the current .NET 10 desktop runtime.");
        Add(checks, "storage", "Local application storage", true, IsWritable(stateRoot), stateRoot,
            "Allow Foundry to write to local application data.");

        var native = NativeToolchainReadinessService.Inspect(
            visualStudioInstallationRoot,
            cmakeExecutablePath);
        foreach (var check in native.Checks)
        {
            Add(
                checks,
                check.Id,
                check.Name,
                false,
                check.IsReady,
                check.Details,
                check.RecommendedAction);
        }

        return new(
            checks.Where(item => item.Required).All(item => item.IsReady),
            native.IsReady,
            checks);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Add(
        List<FoundryProductCheck> checks,
        string id,
        string name,
        bool required,
        bool ready,
        string details,
        string action) =>
        checks.Add(new(id, name, required, ready, details, action));
}
