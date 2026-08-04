using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Core.Projects;

public static class FoundryProjectValidator
{
    private static readonly Regex ProjectIdPattern = new(
        @"^[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex SemanticVersionPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex AssemblyNamePattern = new(
        @"^[A-Za-z_][A-Za-z0-9_.-]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex QualifiedTypeNamePattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MethodNamePattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ObsModuleNamePattern = new(
        @"^[a-z0-9][a-z0-9_-]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ObsComponentIdPattern = new(
        @"^[a-z0-9][a-z0-9._-]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<FoundryDiagnostic> Validate(
        FoundryProjectManifest manifest,
        string? projectPath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var diagnostics = new List<FoundryDiagnostic>();

        if (manifest.SchemaVersion != FoundryProjectManifest.CurrentSchemaVersion)
        {
            Add(
                diagnostics,
                "CFP0001",
                $"Schema version {manifest.SchemaVersion} is not supported. Expected {FoundryProjectManifest.CurrentSchemaVersion}.",
                projectPath,
                "$.schemaVersion",
                "Open the project with a compatible Foundry version or migrate it explicitly.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            Add(
                diagnostics,
                "CFP0002",
                "Project name is required.",
                projectPath,
                "$.name",
                "Set name to a human-readable project name.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            Add(
                diagnostics,
                "CFP0003",
                "Project ID is required.",
                projectPath,
                "$.id",
                "Set id to a stable reverse-DNS identifier.");
        }
        else if (!ProjectIdPattern.IsMatch(manifest.Id))
        {
            Add(
                diagnostics,
                "CFP0008",
                "Project ID must be a lowercase reverse-DNS identifier.",
                projectPath,
                "$.id",
                "Use a value such as com.example.my-extension.");
        }

        if (manifest.Target is null || string.IsNullOrWhiteSpace(manifest.Target.Provider))
        {
            Add(
                diagnostics,
                "CFP0004",
                "Target provider is required.",
                projectPath,
                "$.target.provider",
                "Set target.provider to the selected target provider.");
        }

        if (manifest.Target is null || string.IsNullOrWhiteSpace(manifest.Target.Profile))
        {
            Add(
                diagnostics,
                "CFP0005",
                "Target profile is required.",
                projectPath,
                "$.target.profile",
                "Set target.profile to an explicit compatibility profile.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            !SemanticVersionPattern.IsMatch(manifest.Version))
        {
            Add(
                diagnostics,
                "CFP0006",
                "Project version must be a valid semantic version.",
                projectPath,
                "$.version",
                "Use a version such as 0.1.0 or 1.0.0-beta.1.");
        }

        ValidateOutputs(manifest.Outputs, projectPath, diagnostics);
        ValidateManagedBuild(manifest, projectPath, diagnostics);
        ValidateCphInlineBridge(manifest, projectPath, diagnostics);
        ValidateStreamerBotPackage(manifest, projectPath, diagnostics);
        ValidateObsPlugin(manifest, projectPath, diagnostics);
        ValidateTestDefinition(manifest, projectPath, diagnostics);
        ValidateComponents(manifest, projectPath, diagnostics);
        ValidatePublishing(manifest, projectPath, diagnostics);
        return diagnostics;
    }

    private static void ValidatePublishing(
        FoundryProjectManifest manifest,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        var publishing = manifest.Publishing;
        if (publishing is null) return;
        if (string.IsNullOrWhiteSpace(publishing.PackageName) ||
            publishing.PackageName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-')))
            Add(diagnostics, "CFP0060", "Publishing packageName must use letters, digits, dots, or hyphens.", projectPath, "$.publishing.packageName", "Use a portable name such as creator-extension.");
        if (string.IsNullOrWhiteSpace(publishing.Summary) || publishing.Authors is null or { Count: 0 } || publishing.Authors.Any(string.IsNullOrWhiteSpace))
            Add(diagnostics, "CFP0061", "Publishing summary and at least one author are required.", projectPath, "$.publishing", "Complete the release metadata editor.");
        if (!IsSafePublishingFile(publishing.LicenseFile) || !IsSafePublishingFile(publishing.ChangelogFile))
            Add(diagnostics, "CFP0062", "Licence and changelog must be safe project-relative .txt or .md paths.", projectPath, "$.publishing", "Use paths such as LICENSE.txt and CHANGELOG.md.");
        if (!IsOptionalWebUri(publishing.Homepage) || !IsOptionalWebUri(publishing.Repository))
            Add(diagnostics, "CFP0063", "Publishing links must be absolute HTTP or HTTPS URLs.", projectPath, "$.publishing", "Use a complete https:// URL or leave the field empty.");
        var dependencyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in publishing.Dependencies ?? [])
        {
            if (string.IsNullOrWhiteSpace(dependency.Name) || string.IsNullOrWhiteSpace(dependency.Version) ||
                dependency.Kind is not ("runtime" or "library" or "tool") || !dependencyNames.Add(dependency.Name))
                Add(diagnostics, "CFP0064", "Publishing dependencies require unique names, versions, and a supported kind.", projectPath, "$.publishing.dependencies", "Use runtime, library, or tool and keep one entry per dependency.");
            if (!IsOptionalWebUri(dependency.Source))
                Add(diagnostics, "CFP0065", $"Dependency '{dependency.Name}' has an invalid source URL.", projectPath, "$.publishing.dependencies", "Use a complete HTTP/HTTPS source URL.");
        }
        var signing = publishing.Signing ?? new FoundrySigningConfiguration();
        if (signing.Enabled && (string.IsNullOrWhiteSpace(signing.ToolPath) || string.IsNullOrWhiteSpace(signing.CertificateThumbprint)))
            Add(diagnostics, "CFP0066", "Enabled code signing requires a signtool path and certificate thumbprint.", projectPath, "$.publishing.signing", "Configure both fields or disable signing.");
        if (signing.Enabled && !IsOptionalWebUri(signing.TimestampUrl))
            Add(diagnostics, "CFP0067", "The signing timestamp URL must use HTTP or HTTPS.", projectPath, "$.publishing.signing.timestampUrl", "Use the timestamp service URL supplied by the certificate provider.");
    }

    private static bool IsSafePublishingFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal) &&
        Path.GetExtension(path) is ".md" or ".txt";

    private static bool IsOptionalWebUri(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static void ValidateComponents(
        FoundryProjectManifest manifest,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in manifest.Components ?? [])
        {
            if (string.IsNullOrWhiteSpace(component.Id) || !ProjectIdPattern.IsMatch(component.Id))
            {
                Add(diagnostics, "CFP0052", $"Component ID '{component.Id}' is invalid.", projectPath, "$.components", "Use a lowercase reverse-DNS component ID.");
            }
            else if (!ids.Add(component.Id))
            {
                Add(diagnostics, "CFP0053", $"Component '{component.Id}' is declared more than once.", projectPath, "$.components", "Keep one reference for each installed component.");
            }

            if (string.IsNullOrWhiteSpace(component.Version) || !SemanticVersionPattern.IsMatch(component.Version))
            {
                Add(diagnostics, "CFP0054", $"Component '{component.Id}' has an invalid version.", projectPath, "$.components", "Use a semantic version such as 1.0.0.");
            }

            if (component.Sources is null || component.Sources.Count == 0 || component.Sources.Any(path =>
                    string.IsNullOrWhiteSpace(path) ||
                    Path.IsPathRooted(path) ||
                    path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal)))
            {
                Add(diagnostics, "CFP0055", $"Component '{component.Id}' must list safe project-relative source files.", projectPath, "$.components", "Remove absolute paths and parent traversal.");
            }
        }
    }

    private static void ValidateTestDefinition(
        FoundryProjectManifest manifest,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        if (manifest.TestDefinition is null)
        {
            return;
        }

        if (!IsSafeRelativeDefinitionPath(manifest.TestDefinition))
        {
            Add(
                diagnostics,
                "CFP0050",
                "testDefinition must be a project-relative .json path without parent traversal.",
                projectPath,
                "$.testDefinition",
                "Use a path such as tests/foundry-tests.json.");
        }

        if (string.Equals(manifest.Target?.Provider, "streamerbot", StringComparison.Ordinal) &&
            !manifest.Features.MockRuntime)
        {
            Add(
                diagnostics,
                "CFP0051",
                "Streamer.bot tests require the mock runtime feature.",
                projectPath,
                "$.features.mockRuntime",
                "Set features.mockRuntime to true.");
        }
    }

    private static void ValidateObsPlugin(
        FoundryProjectManifest manifest,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        var requestsPlugin = manifest.Outputs?.Contains(
            FoundryOutputKinds.ObsPlugin,
            StringComparer.Ordinal) == true;
        var requestsPackage = manifest.Outputs?.Contains(
            FoundryOutputKinds.ObsPluginPackage,
            StringComparer.Ordinal) == true;
        if (!requestsPlugin && !requestsPackage)
        {
            return;
        }

        if (!string.Equals(manifest.Target?.Provider, "obsstudio", StringComparison.Ordinal))
        {
            Add(diagnostics, "CFP0028", "OBS plugin outputs require the obsstudio target provider.", projectPath, "$.target.provider", "Set target.provider to obsstudio.");
        }

        if (!string.Equals(manifest.Target?.Profile, "32.x-windows-x64", StringComparison.Ordinal))
        {
            Add(diagnostics, "CFP0029", "The OBS plugin foundation supports the verified 32.x-windows-x64 profile.", projectPath, "$.target.profile", "Use 32.x-windows-x64 until another profile has been compatibility-tested.");
        }

        if (requestsPackage && !requestsPlugin)
        {
            Add(diagnostics, "CFP0030", "obsPluginPackage requires the obsPlugin binary output.", projectPath, "$.outputs", "Add obsPlugin to outputs.");
        }

        if (manifest.Outputs!.Any(output => output is
                FoundryOutputKinds.ManagedLibrary or
                FoundryOutputKinds.CphInlineBridge or
                FoundryOutputKinds.StreamerBotPackage))
        {
            Add(diagnostics, "CFP0031", "OBS and Streamer.bot outputs cannot be mixed in one schema-v1 project.", projectPath, "$.outputs", "Create a separate obsstudio project for the native plugin.");
        }

        if (manifest.NativeBuild is null)
        {
            Add(diagnostics, "CFP0032", "Native build settings are required for an OBS plugin.", projectPath, "$.nativeBuild", "Declare the C language, x64 architecture, cmake-msvc toolchain, and sources.");
        }
        else
        {
            var build = manifest.NativeBuild;
            if (!string.Equals(build.Language, FoundryNativeBuild.SupportedLanguage, StringComparison.Ordinal) ||
                !string.Equals(build.Architecture, FoundryNativeBuild.SupportedArchitecture, StringComparison.Ordinal) ||
                !string.Equals(build.Toolchain, FoundryNativeBuild.SupportedToolchain, StringComparison.Ordinal))
            {
                Add(diagnostics, "CFP0033", "Native build settings are outside the Phase 8 compatibility contract.", projectPath, "$.nativeBuild", "Use language c17, architecture x64, and toolchain cmake-msvc.");
            }

            if (build.Sources is null || build.Sources.Count == 0)
            {
                Add(diagnostics, "CFP0034", "At least one native C source is required.", projectPath, "$.nativeBuild.sources", "Add a project-relative .c source path.");
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < build.Sources.Count; index++)
                {
                    var source = build.Sources[index];
                    var path = $"$.nativeBuild.sources[{index}]";
                    if (!IsSafeRelativeNativeSourcePath(source))
                    {
                        Add(diagnostics, "CFP0035", $"Native source path '{source}' must be a project-relative .c path without parent traversal.", projectPath, path, "Use a path such as src/plugin.c.");
                    }
                    else if (!seen.Add(source))
                    {
                        Add(diagnostics, "CFP0036", $"Native source path '{source}' is duplicated.", projectPath, path, "Remove the duplicate source entry.");
                    }
                }
            }
        }

        if (manifest.ObsPlugin is null)
        {
            Add(diagnostics, "CFP0037", "OBS module metadata is required.", projectPath, "$.obsPlugin", "Declare contract, moduleName, entrySymbol, displayName, author, description, and apiVersion.");
            return;
        }

        var plugin = manifest.ObsPlugin;
        var isMinimalContract = string.Equals(
            plugin.Contract,
            FoundryObsPlugin.MinimalContract,
            StringComparison.Ordinal);
        var isSdkContract = string.Equals(
            plugin.Contract,
            FoundryObsPlugin.SdkContract,
            StringComparison.Ordinal);
        if (!isMinimalContract && !isSdkContract)
        {
            Add(diagnostics, "CFP0038", $"OBS module contract '{plugin.Contract}' is not supported.", projectPath, "$.obsPlugin.contract", $"Use {FoundryObsPlugin.MinimalContract} or {FoundryObsPlugin.SdkContract}.");
        }

        if (!ObsModuleNamePattern.IsMatch(plugin.ModuleName))
        {
            Add(diagnostics, "CFP0039", "OBS moduleName must contain lowercase letters, digits, hyphens, or underscores.", projectPath, "$.obsPlugin.moduleName", "Use a stable name such as creators-forge-probe.");
        }

        if (!MethodNamePattern.IsMatch(plugin.EntrySymbol) || IsCSharpReservedKeyword(plugin.EntrySymbol))
        {
            Add(diagnostics, "CFP0040", "OBS entrySymbol must be a portable C identifier.", projectPath, "$.obsPlugin.entrySymbol", "Use foundry_obs_plugin_load.");
        }

        if (string.IsNullOrWhiteSpace(plugin.DisplayName) ||
            string.IsNullOrWhiteSpace(plugin.Author) ||
            string.IsNullOrWhiteSpace(plugin.Description))
        {
            Add(diagnostics, "CFP0041", "OBS displayName, author, and description are required.", projectPath, "$.obsPlugin", "Provide reviewable module metadata.");
        }

        var expectedApiVersion = isSdkContract
            ? FoundryObsPlugin.SupportedSdkVersion
            : FoundryObsPlugin.MinimalApiVersion;
        if (!string.Equals(plugin.ApiVersion, expectedApiVersion, StringComparison.Ordinal))
        {
            Add(diagnostics, "CFP0042", $"OBS API version '{plugin.ApiVersion}' does not match the selected module contract.", projectPath, "$.obsPlugin.apiVersion", $"Use {expectedApiVersion}.");
        }

        if (isSdkContract && !string.Equals(
                plugin.SdkVersion,
                FoundryObsPlugin.SupportedSdkVersion,
                StringComparison.Ordinal))
        {
            Add(diagnostics, "CFP0043", "The libobs module contract requires the pinned OBS SDK.", projectPath, "$.obsPlugin.sdkVersion", $"Use {FoundryObsPlugin.SupportedSdkVersion}.");
        }
        else if (isMinimalContract && plugin.SdkVersion is not null)
        {
            Add(diagnostics, "CFP0044", "The minimal ABI contract cannot declare an OBS SDK.", projectPath, "$.obsPlugin.sdkVersion", $"Remove sdkVersion or use {FoundryObsPlugin.SdkContract}.");
        }

        if (plugin.Design is { } design)
        {
            if (!isSdkContract)
            {
                Add(diagnostics, "CFP0045", "OBS designer templates require the libobs SDK contract.", projectPath, "$.obsPlugin.design", $"Use {FoundryObsPlugin.SdkContract}.");
            }

            if (!FoundryObsDesign.SupportedTemplates.Contains(design.Template))
            {
                Add(diagnostics, "CFP0046", $"OBS designer template '{design.Template}' is not supported.", projectPath, "$.obsPlugin.design.template", $"Use one of: {string.Join(", ", FoundryObsDesign.SupportedTemplates.Order(StringComparer.Ordinal))}.");
            }

            if (!IsSafeRelativeNativeSourcePath(design.Source) ||
                manifest.NativeBuild?.Sources.Contains(design.Source, StringComparer.OrdinalIgnoreCase) != true)
            {
                Add(diagnostics, "CFP0047", "The OBS designer source must be a declared native C source.", projectPath, "$.obsPlugin.design.source", "Choose a .c path listed in nativeBuild.sources.");
            }

            if (!ObsComponentIdPattern.IsMatch(design.ComponentId))
            {
                Add(diagnostics, "CFP0048", "OBS componentId must contain lowercase letters, digits, dots, hyphens, or underscores.", projectPath, "$.obsPlugin.design.componentId", "Use a stable ID such as dev.creator.my-filter.");
            }

            if (string.IsNullOrWhiteSpace(design.ComponentName))
            {
                Add(diagnostics, "CFP0049", "OBS componentName is required.", projectPath, "$.obsPlugin.design.componentName", "Provide the name shown in OBS Studio.");
            }
        }
    }

    private static void ValidateStreamerBotPackage(
        FoundryProjectManifest manifest,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        var requestsPackage =
            manifest.Outputs?.Contains(
                FoundryOutputKinds.StreamerBotPackage,
                StringComparer.Ordinal) == true;
        if (!requestsPackage)
        {
            return;
        }

        if (!string.Equals(
                manifest.Target?.Provider,
                "streamerbot",
                StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "CFP0024",
                "The streamerBotPackage output requires the streamerbot target provider.",
                projectPath,
                "$.target.provider",
                "Set target.provider to streamerbot or remove streamerBotPackage.");
        }

        if (manifest.Target?.Profile is not string profile ||
            !Compatibility.FoundryStreamerBotProfiles.Supported.Contains(profile))
        {
            Add(
                diagnostics,
                "CFP0025",
                "The Streamer.bot package output requires a supported compatibility profile.",
                projectPath,
                "$.target.profile",
                $"Use {string.Join(", ", Compatibility.FoundryStreamerBotProfiles.Ordered)}. The writer emits the cross-compatible stable-v23 package.");
        }

        if (manifest.Outputs?.Contains(
                FoundryOutputKinds.CphInlineBridge,
                StringComparer.Ordinal) != true)
        {
            Add(
                diagnostics,
                "CFP0026",
                "The streamerBotPackage output requires cphInlineBridge.",
                projectPath,
                "$.outputs",
                "Add managedLibrary and cphInlineBridge to outputs.");
        }

        if (!IsSafeRelativeDefinitionPath(manifest.TargetDefinition))
        {
            Add(
                diagnostics,
                "CFP0027",
                "targetDefinition must be a project-relative .json path without parent traversal.",
                projectPath,
                "$.targetDefinition",
                "Use a path such as streamerbot/streamerbot.json.");
        }
    }

    private static void ValidateCphInlineBridge(
        FoundryProjectManifest manifest,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        var requestsBridge =
            manifest.Outputs?.Contains(
                FoundryOutputKinds.CphInlineBridge,
                StringComparer.Ordinal) == true;
        var requestsManagedLibrary =
            manifest.Outputs?.Contains(
                FoundryOutputKinds.ManagedLibrary,
                StringComparer.Ordinal) == true;

        if (requestsBridge && !requestsManagedLibrary)
        {
            Add(
                diagnostics,
                "CFP0019",
                "The cphInlineBridge output requires managedLibrary.",
                projectPath,
                "$.outputs",
                "Add managedLibrary to outputs.");
        }

        if (requestsBridge &&
            !string.Equals(
                manifest.Target?.Provider,
                "streamerbot",
                StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "CFP0020",
                "The CPHInline bridge requires the streamerbot target provider.",
                projectPath,
                "$.target.provider",
                "Set target.provider to streamerbot or remove cphInlineBridge.");
        }

        if (manifest.CphInlineBridge is null)
        {
            if (requestsBridge)
            {
                Add(
                    diagnostics,
                    "CFP0018",
                    "CPHInline bridge settings are required for the cphInlineBridge output.",
                    projectPath,
                    "$.cphInlineBridge",
                    "Declare contract, entryType, and entryMethod.");
            }

            return;
        }

        var bridge = manifest.CphInlineBridge;
        if (!string.Equals(
                bridge.Contract,
                FoundryCphInlineBridge.SupportedContract,
                StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "CFP0021",
                $"CPHInline bridge contract '{bridge.Contract}' is not supported.",
                projectPath,
                "$.cphInlineBridge.contract",
                $"Use {FoundryCphInlineBridge.SupportedContract}.");
        }

        if (!QualifiedTypeNamePattern.IsMatch(bridge.EntryType) ||
            bridge.EntryType.Split('.').Any(IsCSharpReservedKeyword))
        {
            Add(
                diagnostics,
                "CFP0022",
                "CPHInline bridge entryType must be a fully qualified C# type name.",
                projectPath,
                "$.cphInlineBridge.entryType",
                "Use a value such as MyExtension.EntryPoint.");
        }

        if (!MethodNamePattern.IsMatch(bridge.EntryMethod) ||
            IsCSharpReservedKeyword(bridge.EntryMethod))
        {
            Add(
                diagnostics,
                "CFP0023",
                "CPHInline bridge entryMethod must be a C# method name.",
                projectPath,
                "$.cphInlineBridge.entryMethod",
                "Use a value such as Execute.");
        }
    }

    private static void ValidateManagedBuild(
        FoundryProjectManifest manifest,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        var requestsManagedLibrary =
            manifest.Outputs?.Contains(FoundryOutputKinds.ManagedLibrary, StringComparer.Ordinal) ==
            true;

        if (manifest.ManagedBuild is null)
        {
            if (requestsManagedLibrary)
            {
                Add(
                    diagnostics,
                    "CFP0011",
                    "Managed build settings are required for the managedLibrary output.",
                    projectPath,
                    "$.managedBuild",
                    "Declare targetFramework, languageVersion, assemblyName, and sources.");
            }

            return;
        }

        var build = manifest.ManagedBuild;
        if (string.IsNullOrWhiteSpace(build.AssemblyName) ||
            !AssemblyNamePattern.IsMatch(build.AssemblyName))
        {
            Add(
                diagnostics,
                "CFP0012",
                "Managed assembly name is invalid.",
                projectPath,
                "$.managedBuild.assemblyName",
                "Use letters, digits, dots, hyphens, and underscores without a path.");
        }

        if (!string.Equals(
                build.TargetFramework,
                FoundryManagedBuild.SupportedTargetFramework,
                StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "CFP0013",
                $"Managed target framework '{build.TargetFramework}' is not supported.",
                projectPath,
                "$.managedBuild.targetFramework",
                $"Use {FoundryManagedBuild.SupportedTargetFramework} for the current Streamer.bot compatibility contract.");
        }

        if (!string.Equals(
                build.LanguageVersion,
                FoundryManagedBuild.SupportedLanguageVersion,
                StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "CFP0014",
                $"C# language version '{build.LanguageVersion}' is not supported.",
                projectPath,
                "$.managedBuild.languageVersion",
                $"Use C# {FoundryManagedBuild.SupportedLanguageVersion} for the current Streamer.bot compatibility contract.");
        }

        if (build.Sources is null || build.Sources.Count == 0)
        {
            Add(
                diagnostics,
                "CFP0015",
                "At least one managed source file is required.",
                projectPath,
                "$.managedBuild.sources",
                "Add a project-relative .cs source path.");
            return;
        }

        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < build.Sources.Count; index++)
        {
            var source = build.Sources[index];
            var jsonPath = $"$.managedBuild.sources[{index}]";

            if (!IsSafeRelativeSourcePath(source))
            {
                Add(
                    diagnostics,
                    "CFP0016",
                    $"Managed source path '{source}' must be a project-relative .cs path without parent traversal.",
                    projectPath,
                    jsonPath,
                    "Use a path such as src/Extension/EntryPoint.cs.");
            }
            else if (!seenSources.Add(source))
            {
                Add(
                    diagnostics,
                    "CFP0017",
                    $"Managed source path '{source}' is duplicated.",
                    projectPath,
                    jsonPath,
                    "Remove the duplicate source entry.");
            }
        }
    }

    private static void ValidateOutputs(
        IReadOnlyList<string>? outputs,
        string? projectPath,
        ICollection<FoundryDiagnostic> diagnostics)
    {
        if (outputs is null || outputs.Count == 0)
        {
            Add(
                diagnostics,
                "CFP0007",
                "At least one project output is required.",
                projectPath,
                "$.outputs",
                "Add managedLibrary, cphInlineBridge, or streamerBotPackage.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < outputs.Count; index++)
        {
            var output = outputs[index];
            var path = $"$.outputs[{index}]";

            if (!FoundryOutputKinds.Supported.Contains(output))
            {
                Add(
                    diagnostics,
                    "CFP0009",
                    $"Project output '{output}' is not supported.",
                    projectPath,
                    path,
                    "Use managedLibrary, cphInlineBridge, or streamerBotPackage.");
            }
            else if (!seen.Add(output))
            {
                Add(
                    diagnostics,
                    "CFP0010",
                    $"Project output '{output}' is duplicated.",
                    projectPath,
                    path,
                    "Remove the duplicate output.");
            }
        }
    }

    private static void Add(
        ICollection<FoundryDiagnostic> diagnostics,
        string code,
        string message,
        string? projectPath,
        string jsonPath,
        string suggestedFix)
    {
        var location = projectPath is null
            ? null
            : new FoundryDiagnosticLocation(projectPath, jsonPath);
        diagnostics.Add(new(
            code,
            FoundryDiagnosticSeverity.Error,
            message,
            location,
            suggestedFix));
    }

    private static bool IsSafeRelativeDefinitionPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) &&
        !Path.IsPathRooted(path) &&
        !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");

    private static bool IsSafeRelativeNativeSourcePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetExtension(path), ".c", StringComparison.OrdinalIgnoreCase) &&
        !Path.IsPathRooted(path) &&
        !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");

    private static bool IsSafeRelativeSourcePath(string source)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            source.StartsWith('/') ||
            source.StartsWith('\\') ||
            source.Contains(':') ||
            source.Contains('\0') ||
            !source.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return source
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
    }

    private static bool IsCSharpReservedKeyword(string value) => value is
        "abstract" or "as" or "base" or "bool" or "break" or "byte" or "case" or
        "catch" or "char" or "checked" or "class" or "const" or "continue" or
        "decimal" or "default" or "delegate" or "do" or "double" or "else" or
        "enum" or "event" or "explicit" or "extern" or "false" or "finally" or
        "fixed" or "float" or "for" or "foreach" or "goto" or "if" or "implicit" or
        "in" or "int" or "interface" or "internal" or "is" or "lock" or "long" or
        "namespace" or "new" or "null" or "object" or "operator" or "out" or
        "override" or "params" or "private" or "protected" or "public" or
        "readonly" or "ref" or "return" or "sbyte" or "sealed" or "short" or
        "sizeof" or "stackalloc" or "static" or "string" or "struct" or "switch" or
        "this" or "throw" or "true" or "try" or "typeof" or "uint" or "ulong" or
        "unchecked" or "unsafe" or "ushort" or "using" or "virtual" or "void" or
        "volatile" or "while";
}
