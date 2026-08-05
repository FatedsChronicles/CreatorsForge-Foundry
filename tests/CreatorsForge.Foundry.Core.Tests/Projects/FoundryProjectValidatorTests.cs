using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Core.Tests.Projects;

public sealed class FoundryProjectValidatorTests
{
    [Fact]
    public void AcceptsVerifiedObsPluginFoundationManifest()
    {
        var manifest = new FoundryProjectManifest
        {
            Name = "OBS Plugin",
            Id = "dev.creatorsforge.obs-plugin",
            Version = "0.1.0",
            Target = new FoundryTarget { Provider = "obsstudio", Profile = "32.x-windows-x64" },
            NativeBuild = new FoundryNativeBuild { Sources = ["src/plugin.c"] },
            ObsPlugin = new FoundryObsPlugin
            {
                ModuleName = "creators-forge-plugin",
                DisplayName = "Creators Forge Plugin",
                Author = "Creators Forge",
                Description = "A native OBS module.",
            },
            Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
        };

        Assert.Empty(FoundryProjectValidator.Validate(manifest));
    }

    [Fact]
    public void AcceptsPinnedObsSdkModuleManifest()
    {
        var manifest = new FoundryProjectManifest
        {
            Name = "OBS SDK Plugin",
            Id = "dev.creatorsforge.obs-sdk-plugin",
            Version = "0.1.0",
            Target = new FoundryTarget { Provider = "obsstudio", Profile = "32.x-windows-x64" },
            NativeBuild = new FoundryNativeBuild { Sources = ["src/plugin.c"] },
            ObsPlugin = new FoundryObsPlugin
            {
                Contract = FoundryObsPlugin.SdkContract,
                ModuleName = "creators-forge-sdk-plugin",
                DisplayName = "Creators Forge SDK Plugin",
                Author = "Creators Forge",
                Description = "A pinned SDK module.",
                ApiVersion = FoundryObsPlugin.SupportedSdkVersion,
                SdkVersion = FoundryObsPlugin.SupportedSdkVersion,
                Design = new FoundryObsDesign
                {
                    Template = FoundryObsDesign.PassthroughFilterTemplate,
                    Source = "src/plugin.c",
                    ComponentId = "dev.creatorsforge.sdk-filter",
                    ComponentName = "SDK Filter",
                },
            },
            Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
        };

        Assert.Empty(FoundryProjectValidator.Validate(manifest));
    }

    [Fact]
    public void RejectsInvalidObsDesignerMetadata()
    {
        var manifest = new FoundryProjectManifest
        {
            Name = "OBS Designer",
            Id = "dev.creatorsforge.obs-designer",
            Version = "0.1.0",
            Target = new FoundryTarget { Provider = "obsstudio", Profile = "32.x-windows-x64" },
            NativeBuild = new FoundryNativeBuild { Sources = ["src/plugin.c"] },
            ObsPlugin = new FoundryObsPlugin
            {
                Contract = FoundryObsPlugin.SdkContract,
                ModuleName = "obs-designer",
                DisplayName = "OBS Designer",
                Author = "Creators Forge",
                Description = "Designer test.",
                ApiVersion = FoundryObsPlugin.SupportedSdkVersion,
                SdkVersion = FoundryObsPlugin.SupportedSdkVersion,
                Design = new FoundryObsDesign
                {
                    Template = "unknown",
                    Source = "src/other.c",
                    ComponentId = "Invalid ID",
                    ComponentName = " ",
                },
            },
            Outputs = [FoundryOutputKinds.ObsPlugin],
        };

        var codes = FoundryProjectValidator.Validate(manifest).Select(item => item.Code);
        Assert.Equal(["CFP0046", "CFP0047", "CFP0048", "CFP0049"], codes);
    }

    [Fact]
    public void RejectsMixedAndUnverifiedObsPluginContracts()
    {
        var manifest = new FoundryProjectManifest
        {
            Name = "OBS Plugin",
            Id = "dev.creatorsforge.obs-plugin",
            Version = "0.1.0",
            Target = new FoundryTarget { Provider = "obsstudio", Profile = "31.x-windows-x64" },
            NativeBuild = new FoundryNativeBuild { Architecture = "arm64", Sources = ["../plugin.cpp"] },
            ObsPlugin = new FoundryObsPlugin { ModuleName = "Invalid Module", ApiVersion = "31.0.0" },
            Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ManagedLibrary],
        };

        var codes = FoundryProjectValidator.Validate(manifest).Select(item => item.Code).ToHashSet();
        Assert.Contains("CFP0029", codes);
        Assert.Contains("CFP0031", codes);
        Assert.Contains("CFP0033", codes);
        Assert.Contains("CFP0035", codes);
        Assert.Contains("CFP0039", codes);
        Assert.Contains("CFP0042", codes);
    }

    [Fact]
    public void ValidateReturnsNoDiagnosticsForCompleteProject()
    {
        var manifest = CreateValidManifest();

        var diagnostics = FoundryProjectValidator.Validate(manifest, "Sample.foundryproj");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ValidateReturnsStableCodesAndJsonPaths()
    {
        var manifest = CreateValidManifest() with
        {
            SchemaVersion = FoundryProjectManifest.CurrentSchemaVersion + 1,
            Name = " ",
            Id = string.Empty,
        };

        var diagnostics = FoundryProjectValidator.Validate(manifest, "Sample.foundryproj");

        Assert.Equal(["CFP0001", "CFP0002", "CFP0003"], diagnostics.Select(item => item.Code));
        Assert.All(diagnostics, item => Assert.Equal(FoundryDiagnosticSeverity.Error, item.Severity));
        Assert.Equal(
            ["$.schemaVersion", "$.name", "$.id"],
            diagnostics.Select(item => item.Location?.JsonPath));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-01")]
    [InlineData("v1.0.0")]
    public void ValidateRejectsInvalidSemanticVersions(string version)
    {
        var manifest = CreateValidManifest() with { Version = version };

        var diagnostic = Assert.Single(FoundryProjectValidator.Validate(manifest));

        Assert.Equal("CFP0006", diagnostic.Code);
    }

    [Fact]
    public void ValidateRejectsUnknownAndDuplicateOutputs()
    {
        var manifest = CreateValidManifest() with
        {
            Outputs =
            [
                FoundryOutputKinds.ManagedLibrary,
                "unknownOutput",
                FoundryOutputKinds.ManagedLibrary,
            ],
        };

        var diagnostics = FoundryProjectValidator.Validate(manifest);

        Assert.Equal(["CFP0009", "CFP0010"], diagnostics.Select(item => item.Code));
    }

    [Fact]
    public void ValidateRejectsUnsafeManagedBuildInputs()
    {
        var manifest = CreateValidManifest() with
        {
            ManagedBuild = new FoundryManagedBuild
            {
                TargetFramework = "net10.0",
                LanguageVersion = "14.0",
                AssemblyName = "../unsafe",
                Sources = ["../Outside.cs", "src/Valid.cs", "SRC/VALID.CS"],
            },
        };

        var diagnostics = FoundryProjectValidator.Validate(manifest);

        Assert.Equal(
            ["CFP0012", "CFP0013", "CFP0014", "CFP0016", "CFP0017"],
            diagnostics.Select(item => item.Code));
    }

    [Fact]
    public void ValidateAcceptsExplicitBridgeContract()
    {
        var manifest = CreateValidManifest() with
        {
            CphInlineBridge = CreateValidBridge(),
            Outputs =
            [
                FoundryOutputKinds.ManagedLibrary,
                FoundryOutputKinds.CphInlineBridge,
            ],
        };

        var diagnostics = FoundryProjectValidator.Validate(manifest);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ValidateRejectsInvalidBridgeContractAndEntryPoint()
    {
        var manifest = CreateValidManifest() with
        {
            CphInlineBridge = new FoundryCphInlineBridge
            {
                Contract = "future-v2",
                EntryType = "NotQualified",
                EntryMethod = "Run()",
            },
            Outputs =
            [
                FoundryOutputKinds.ManagedLibrary,
                FoundryOutputKinds.CphInlineBridge,
            ],
        };

        var diagnostics = FoundryProjectValidator.Validate(manifest);

        Assert.Equal(
            ["CFP0021", "CFP0022", "CFP0023"],
            diagnostics.Select(item => item.Code));
    }

    [Fact]
    public void ValidateReportsAllMissingBridgeRequirements()
    {
        var manifest = CreateValidManifest() with
        {
            Target = new FoundryTarget
            {
                Provider = "obs",
                Profile = "future",
            },
            CphInlineBridge = null,
            Outputs = [FoundryOutputKinds.CphInlineBridge],
        };

        var diagnostics = FoundryProjectValidator.Validate(manifest);

        Assert.Equal(
            ["CFP0019", "CFP0020", "CFP0018"],
            diagnostics.Select(item => item.Code));
    }

    [Theory]
    [InlineData("1.0.4-stable")]
    [InlineData("1.0.5-alpha.34")]
    [InlineData("1.0.5-beta.1")]
    [InlineData("1.0.5-beta.6")]
    public void ValidateAcceptsPackageForSupportedProfiles(string profile)
    {
        var manifest = CreateValidManifest() with
        {
            Target = new FoundryTarget
            {
                Provider = "streamerbot",
                Profile = profile,
            },
            CphInlineBridge = CreateValidBridge(),
            TargetDefinition = "streamerbot/streamerbot.json",
            Outputs =
            [
                FoundryOutputKinds.ManagedLibrary,
                FoundryOutputKinds.CphInlineBridge,
                FoundryOutputKinds.StreamerBotPackage,
            ],
        };

        Assert.Empty(FoundryProjectValidator.Validate(manifest));
    }

    [Fact]
    public void ValidateAcceptsStreamerBotMockTestDefinition()
    {
        var manifest = CreateValidManifest() with
        {
            Features = new FoundryFeatures { MockRuntime = true },
            CphInlineBridge = CreateValidBridge(),
            TestDefinition = "tests/foundry-tests.json",
            Outputs =
            [
                FoundryOutputKinds.ManagedLibrary,
                FoundryOutputKinds.CphInlineBridge,
            ],
        };

        Assert.Empty(FoundryProjectValidator.Validate(manifest));
    }

    [Fact]
    public void ValidateRejectsUnsafeTestDefinitionAndDisabledMockRuntime()
    {
        var manifest = CreateValidManifest() with
        {
            TestDefinition = "../outside.json",
        };

        var diagnostics = FoundryProjectValidator.Validate(manifest);

        Assert.Equal(["CFP0050", "CFP0051"], diagnostics.Select(item => item.Code));
    }

    [Fact]
    public void ValidateAcceptsCompletePublishingMetadata()
    {
        var manifest = CreateValidManifest() with
        {
            Publishing = new FoundryPublishing
            {
                PackageName = "com.example.valid-release",
                Summary = "A release-ready extension.",
                Authors = ["Example Author"],
                Homepage = "https://example.com",
                Repository = "https://example.com/source",
                Tags = ["streaming"],
                Dependencies = [new() { Kind = "library", Name = "Example Library", Version = "1.0.0", License = "MIT" }],
            },
        };

        Assert.Empty(FoundryProjectValidator.Validate(manifest));
    }

    [Fact]
    public void ValidateRejectsUnsafePublishingAndIncompleteSigningMetadata()
    {
        var manifest = CreateValidManifest() with
        {
            Publishing = new FoundryPublishing
            {
                PackageName = "unsafe/name",
                Summary = " ",
                Authors = [],
                LicenseFile = "../LICENSE.txt",
                ChangelogFile = "notes.exe",
                Homepage = "file:///local",
                Dependencies = [new() { Kind = "unknown", Name = " ", Version = " " }],
                Signing = new() { Enabled = true, TimestampUrl = "file:///timestamp" },
            },
        };

        var codes = FoundryProjectValidator.Validate(manifest).Select(item => item.Code).ToHashSet();
        Assert.Contains("CFP0060", codes);
        Assert.Contains("CFP0061", codes);
        Assert.Contains("CFP0062", codes);
        Assert.Contains("CFP0063", codes);
        Assert.Contains("CFP0064", codes);
        Assert.Contains("CFP0067", codes);
    }

    [Fact]
    public void ValidateAcceptsStaticWebPreviewMetadata()
    {
        var manifest = CreateValidManifest() with
        {
            Preview = new FoundryPreview
            {
                Kind = FoundryPreview.StaticWebKind,
                Source = "ui/index.html",
                Width = 1280,
                Height = 720,
            },
        };

        Assert.Empty(FoundryProjectValidator.Validate(manifest));
    }

    [Fact]
    public void ValidateAcceptsDeclaredWinFormsPreview()
    {
        var manifest = CreateValidManifest() with
        {
            Features = new FoundryFeatures { WinForms = true },
            Preview = new FoundryPreview
            {
                Kind = FoundryPreview.WinFormsKind,
                Source = "src/EntryPoint.cs",
                Width = 800,
                Height = 600,
            },
        };

        Assert.Empty(FoundryProjectValidator.Validate(manifest));
    }

    [Fact]
    public void ValidateRejectsUnsafeUnsupportedOrIneligiblePreview()
    {
        var manifest = CreateValidManifest() with
        {
            Preview = new FoundryPreview
            {
                Kind = FoundryPreview.WinFormsKind,
                Source = "../outside.html",
                Width = 100,
                Height = 5000,
            },
        };

        var codes = FoundryProjectValidator.Validate(manifest).Select(item => item.Code).ToHashSet();
        Assert.Contains("CFP0069", codes);
        Assert.Contains("CFP0070", codes);
        Assert.Contains("CFP0071", codes);
    }

    private static FoundryProjectManifest CreateValidManifest() => new()
    {
        Name = "Shoutout Tool",
        Id = "com.creatorsforge.samples.shoutout",
        Version = "0.1.0",
        Target = new FoundryTarget
        {
            Provider = "streamerbot",
            Profile = "1.0.4-stable",
        },
        ManagedBuild = new FoundryManagedBuild
        {
            AssemblyName = "CreatorsForge.Samples.Shoutout",
            Sources = ["src/EntryPoint.cs"],
        },
        Outputs = [FoundryOutputKinds.ManagedLibrary],
    };

    private static FoundryCphInlineBridge CreateValidBridge() => new()
    {
        Contract = FoundryCphInlineBridge.SupportedContract,
        EntryType = "CreatorsForge.Samples.Shoutout.EntryPoint",
        EntryMethod = "Execute",
    };
}
