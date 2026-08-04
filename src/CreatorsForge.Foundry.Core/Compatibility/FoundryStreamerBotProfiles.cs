namespace CreatorsForge.Foundry.Core.Compatibility;

public static class FoundryStreamerBotProfiles
{
    public const string Stable104 = "1.0.4-stable";
    public const string Alpha10534 = "1.0.5-alpha.34";
    public const string Beta1051 = "1.0.5-beta.1";
    public const string Beta1056 = "1.0.5-beta.6";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [Stable104, Alpha10534, Beta1051, Beta1056],
        StringComparer.Ordinal);

    public static IReadOnlyList<string> Ordered { get; } =
        [Stable104, Alpha10534, Beta1051, Beta1056];
}
