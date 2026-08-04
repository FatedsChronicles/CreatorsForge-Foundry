namespace CreatorsForge.Foundry.Core.Compatibility;

public static class FoundryObsCompatibility
{
    public const string ProjectProfile = "32.x-windows-x64";
    public const string PinnedSdkVersion = "32.1.2";
    public const string Runtime3212 = "32.1.2";
    public const string Runtime3221 = "32.2.1";

    public static IReadOnlySet<string> SupportedRuntimeVersions { get; } =
        new HashSet<string>([Runtime3212, Runtime3221], StringComparer.Ordinal);

    public static bool IsSupportedRuntime(string versionText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionText);
        var core = versionText.Split(['-', '+'], 2)[0];
        return SupportedRuntimeVersions.Contains(core);
    }

    public static string SupportedRuntimeDisplay =>
        string.Join(" or ", SupportedRuntimeVersions.Order(StringComparer.Ordinal));
}
