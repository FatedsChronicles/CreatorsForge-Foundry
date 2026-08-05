using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreatorsForge.Foundry.Core.Projects;

/// <summary>
/// The source-first manifest persisted in a <c>.foundryproj</c> file.
/// </summary>
public sealed record FoundryProjectManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Name { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public FoundryTarget? Target { get; init; }

    public FoundryProjectTemplateReference? Template { get; init; }

    public IReadOnlyList<FoundryComponentReference> Components { get; init; } = [];

    public FoundryPublishing? Publishing { get; init; }

    public FoundryFeatures Features { get; init; } = new();

    public FoundryPreview? Preview { get; init; }

    public FoundryManagedBuild? ManagedBuild { get; init; }

    public FoundryNativeBuild? NativeBuild { get; init; }

    public FoundryObsPlugin? ObsPlugin { get; init; }

    public FoundryCphInlineBridge? CphInlineBridge { get; init; }

    /// <summary>
    /// Project-relative structured input consumed by the selected target
    /// package adapter.
    /// </summary>
    public string? TargetDefinition { get; init; }

    /// <summary>
    /// Optional project-relative definition consumed by the Foundry test
    /// runner. Test definitions are source files and are never inferred from
    /// build output.
    /// </summary>
    public string? TestDefinition { get; init; }

    public IReadOnlyList<string> Outputs { get; init; } = [];

    /// <summary>
    /// Retains fields introduced by newer Foundry versions so loading and
    /// rewriting a manifest does not silently discard them.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryPublishing
{
    public string PackageName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Authors { get; init; } = [];
    public string LicenseFile { get; init; } = "LICENSE.txt";
    public string ChangelogFile { get; init; } = "CHANGELOG.md";
    public string? Homepage { get; init; }
    public string? Repository { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<FoundryPublishingDependency> Dependencies { get; init; } = [];
    public FoundrySigningConfiguration Signing { get; init; } = new();
}

public sealed record FoundryPublishingDependency
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Kind { get; init; } = "runtime";
    public string? License { get; init; }
    public string? Source { get; init; }
}

public sealed record FoundrySigningConfiguration
{
    public bool Enabled { get; init; }
    public string? ToolPath { get; init; }
    public string? CertificateThumbprint { get; init; }
    public string? TimestampUrl { get; init; }
}

public sealed record FoundryComponentReference
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public IReadOnlyList<string> Sources { get; init; } = [];
}

public sealed record FoundryProjectTemplateReference
{
    public string Id { get; init; } = string.Empty;

    public string Revision { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record FoundryTarget
{
    public string Provider { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryFeatures
{
    public bool WinForms { get; init; }

    public bool MockRuntime { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryPreview
{
    public const string StaticWebKind = "static-web";
    public const string WinFormsKind = "winforms";
    public const string ObsComponentKind = "obs-component";

    public static IReadOnlySet<string> SupportedKinds { get; } =
        new HashSet<string>(
            [StaticWebKind, WinFormsKind, ObsComponentKind],
            StringComparer.Ordinal);

    public bool Enabled { get; init; } = true;

    public string Kind { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryManagedBuild
{
    public const string SupportedTargetFramework = "net481";
    public const string SupportedLanguageVersion = "7.3";

    public string TargetFramework { get; init; } = SupportedTargetFramework;

    public string LanguageVersion { get; init; } = SupportedLanguageVersion;

    public string AssemblyName { get; init; } = string.Empty;

    public IReadOnlyList<string> Sources { get; init; } = [];

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryCphInlineBridge
{
    public const string SupportedContract = "args-log-v1";

    public string Contract { get; init; } = string.Empty;

    public string EntryType { get; init; } = string.Empty;

    public string EntryMethod { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryNativeBuild
{
    public const string SupportedLanguage = "c17";
    public const string SupportedArchitecture = "x64";
    public const string SupportedToolchain = "cmake-msvc";

    public string Language { get; init; } = SupportedLanguage;

    public string Architecture { get; init; } = SupportedArchitecture;

    public string Toolchain { get; init; } = SupportedToolchain;

    public IReadOnlyList<string> Sources { get; init; } = [];

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryObsPlugin
{
    public const string MinimalContract = "module-load-v1";
    public const string SdkContract = "libobs-module-v1";
    public const string MinimalApiVersion = "32.1.1";
    public const string SupportedSdkVersion = "32.1.2";

    public string Contract { get; init; } = MinimalContract;

    public string ModuleName { get; init; } = string.Empty;

    public string EntrySymbol { get; init; } = "foundry_obs_plugin_load";

    public string DisplayName { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = MinimalApiVersion;

    public string? SdkVersion { get; init; }

    public FoundryObsDesign? Design { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record FoundryObsDesign
{
    public const string ModuleStarterTemplate = "module-starter-v1";
    public const string PassthroughFilterTemplate = "passthrough-filter-v1";
    public const string ConfigurableFilterTemplate = "configurable-filter-v1";
    public const string VideoInputTemplate = "video-input-v1";
    public const string OutputTemplate = "output-v1";

    public static IReadOnlySet<string> SupportedTemplates { get; } =
        new HashSet<string>(
            [
                ModuleStarterTemplate,
                PassthroughFilterTemplate,
                ConfigurableFilterTemplate,
                VideoInputTemplate,
                OutputTemplate,
            ],
            StringComparer.Ordinal);

    public string Template { get; init; } = PassthroughFilterTemplate;

    public string Source { get; init; } = "src/plugin.c";

    public string ComponentId { get; init; } = string.Empty;

    public string ComponentName { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public static class FoundryOutputKinds
{
    public const string ManagedLibrary = "managedLibrary";
    public const string CphInlineBridge = "cphInlineBridge";
    public const string StreamerBotPackage = "streamerBotPackage";
    public const string ObsPlugin = "obsPlugin";
    public const string ObsPluginPackage = "obsPluginPackage";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [ManagedLibrary, CphInlineBridge, StreamerBotPackage, ObsPlugin, ObsPluginPackage],
        StringComparer.Ordinal);
}
