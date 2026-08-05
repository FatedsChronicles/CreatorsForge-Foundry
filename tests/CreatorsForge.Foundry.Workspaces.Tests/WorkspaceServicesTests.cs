using System.Text.Json;
using System.Text.Json.Nodes;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class WorkspaceServicesTests
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Theory]
    [InlineData(WorkspaceProjectItemKind.CSharp, "Feature", "Feature.cs")]
    [InlineData(WorkspaceProjectItemKind.Cpp, "plugin", "plugin.cpp")]
    [InlineData(WorkspaceProjectItemKind.C, "module.c", "module.c")]
    [InlineData(WorkspaceProjectItemKind.Header, "module", "module.h")]
    [InlineData(WorkspaceProjectItemKind.Json, "settings", "settings.json")]
    [InlineData(WorkspaceProjectItemKind.Xml, "layout", "layout.xml")]
    [InlineData(WorkspaceProjectItemKind.Html, "panel", "panel.html")]
    [InlineData(WorkspaceProjectItemKind.Css, "panel", "panel.css")]
    [InlineData(WorkspaceProjectItemKind.JavaScript, "panel", "panel.js")]
    [InlineData(WorkspaceProjectItemKind.Markdown, "README", "README.md")]
    [InlineData(WorkspaceProjectItemKind.Text, "notes", "notes.txt")]
    [InlineData(WorkspaceProjectItemKind.CMake, "CMakeLists", "CMakeLists.txt")]
    public async Task ProjectItemCreationAppliesExpectedExtension(
        WorkspaceProjectItemKind kind,
        string name,
        string expectedName)
    {
        using var temporary = TemporaryDirectory.Create();

        var result = await WorkspaceProjectItemService.CreateAsync(
            temporary.Path,
            temporary.Path,
            name,
            kind);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedName, Path.GetFileName(result.Value!.FullPath));
        Assert.True(File.Exists(result.Value.FullPath));
    }

    [Fact]
    public async Task ProjectItemCreationCreatesFolderAndRejectsOverwriteAndTraversal()
    {
        using var temporary = TemporaryDirectory.Create();
        var source = Path.Combine(temporary.Path, "src");
        Directory.CreateDirectory(source);

        var folder = await WorkspaceProjectItemService.CreateAsync(
            temporary.Path,
            source,
            "Features",
            WorkspaceProjectItemKind.Folder);
        var duplicate = await WorkspaceProjectItemService.CreateAsync(
            temporary.Path,
            source,
            "Features",
            WorkspaceProjectItemKind.Folder);
        var traversal = await WorkspaceProjectItemService.CreateAsync(
            temporary.Path,
            source,
            "../outside",
            WorkspaceProjectItemKind.CSharp);

        Assert.True(folder.IsSuccess);
        Assert.True(Directory.Exists(folder.Value!.FullPath));
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Code == "CFW1103");
        Assert.Contains(traversal.Diagnostics, diagnostic => diagnostic.Code == "CFW1102");
    }

    [Fact]
    public async Task ProjectItemRenameMovesFileAndFolderWithoutOverwriting()
    {
        using var temporary = TemporaryDirectory.Create();
        var source = Path.Combine(temporary.Path, "source.txt");
        var folder = Path.Combine(temporary.Path, "Features");
        await File.WriteAllTextAsync(source, "preserved");
        Directory.CreateDirectory(folder);

        var renamedFile = await WorkspaceProjectItemService.RenameAsync(
            temporary.Path,
            source,
            "renamed.txt");
        var renamedFolder = await WorkspaceProjectItemService.RenameAsync(
            temporary.Path,
            folder,
            "Components");
        var duplicate = await WorkspaceProjectItemService.RenameAsync(
            temporary.Path,
            renamedFile.Value!.FullPath,
            "Components");

        Assert.True(renamedFile.IsSuccess);
        Assert.Equal("preserved", await File.ReadAllTextAsync(renamedFile.Value.FullPath));
        Assert.True(renamedFolder.IsSuccess);
        Assert.True(Directory.Exists(renamedFolder.Value!.FullPath));
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Code == "CFW1114");
    }

    [Fact]
    public void ProjectItemInspectionRejectsRootOutsideAndMissingItems()
    {
        using var temporary = TemporaryDirectory.Create();
        using var outside = TemporaryDirectory.Create();

        var root = WorkspaceProjectItemService.InspectMutable(
            temporary.Path,
            temporary.Path);
        var external = WorkspaceProjectItemService.InspectMutable(
            temporary.Path,
            outside.Path);
        var missing = WorkspaceProjectItemService.InspectMutable(
            temporary.Path,
            Path.Combine(temporary.Path, "missing.txt"));

        Assert.Contains(root.Diagnostics, diagnostic => diagnostic.Code == "CFW1110");
        Assert.Contains(external.Diagnostics, diagnostic => diagnostic.Code == "CFW1110");
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "CFW1111");
    }

    [Fact]
    public async Task CreateOpenEditSaveAndReopenProject()
    {
        using var temporary = TemporaryDirectory.Create();
        var projectDirectory = Path.Combine(temporary.Path, "MyExtension");
        var created = await FoundryWorkspaceService.CreateAsync(
            new(
                projectDirectory,
                "My Extension",
                "com.example.my-extension",
                "1.0.4-stable"),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.NotNull(created.Value);
        Assert.Equal("My Extension", created.Value.Manifest.Name);
        Assert.Contains(
            created.Value.ProjectTree,
            item => item.Name == "src" && item.IsDirectory);
        Assert.Contains(
            FoundryOutputKinds.StreamerBotPackage,
            created.Value.Manifest.Outputs);
        Assert.True(File.Exists(
            Path.Combine(
                projectDirectory,
                "streamerbot",
                "streamerbot.json")));

        var sourcePath = Path.Combine(projectDirectory, "src", "EntryPoint.cs");
        var loaded = await WorkspaceDocumentService.LoadAsync(
            projectDirectory,
            sourcePath,
            CancellationToken.None);
        Assert.True(loaded.IsSuccess);
        Assert.Contains("args-log-v1", File.ReadAllText(created.Value.ProjectPath));

        var updatedText = $"{loaded.Value!.Text}// edited{Environment.NewLine}";
        var saved = await WorkspaceDocumentService.SaveAsync(
            projectDirectory,
            sourcePath,
            updatedText,
            CancellationToken.None);
        Assert.True(saved.IsSuccess);

        var reopened = await FoundryWorkspaceService.OpenAsync(
            created.Value.ProjectPath,
            CancellationToken.None);
        var reloaded = await WorkspaceDocumentService.LoadAsync(
            projectDirectory,
            sourcePath,
            CancellationToken.None);

        Assert.True(reopened.IsSuccess);
        Assert.Equal(updatedText, reloaded.Value?.Text);
    }

    [Fact]
    public async Task CreateObsProjectProducesNativeFoundationFiles()
    {
        using var temporary = TemporaryDirectory.Create();
        var projectDirectory = Path.Combine(temporary.Path, "MyObsPlugin");

        var created = await FoundryWorkspaceService.CreateAsync(
            new(
                projectDirectory,
                "My OBS Plugin",
                "com.example.my-obs-plugin",
                "32.x-windows-x64",
                "obsstudio"));

        Assert.True(created.IsSuccess);
        Assert.Equal("obsstudio", created.Value!.Manifest.Target!.Provider);
        Assert.Equal("com-example-my-obs-plugin", created.Value.Manifest.ObsPlugin!.ModuleName);
        Assert.Equal(FoundryObsPlugin.SdkContract, created.Value.Manifest.ObsPlugin.Contract);
        Assert.Equal("32.1.2", created.Value.Manifest.ObsPlugin.SdkVersion);
        Assert.Equal(
            FoundryObsDesign.PassthroughFilterTemplate,
            created.Value.Manifest.ObsPlugin.Design!.Template);
        Assert.Contains(
            "obs_register_source",
            await File.ReadAllTextAsync(Path.Combine(projectDirectory, "src", "plugin.c")),
            StringComparison.Ordinal);
        Assert.Contains(FoundryOutputKinds.ObsPluginPackage, created.Value.Manifest.Outputs);
        Assert.True(File.Exists(Path.Combine(projectDirectory, "src", "plugin.c")));
        Assert.False(Directory.Exists(Path.Combine(projectDirectory, "streamerbot")));
    }

    [Fact]
    public async Task BuiltInProjectTemplatesCreateValidParameterizedProjects()
    {
        Assert.Equal(
            FoundryProjectTemplateService.Templates.Count,
            FoundryProjectTemplateService.Templates.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        using var temporary = TemporaryDirectory.Create();
        foreach (var template in FoundryProjectTemplateService.Templates)
        {
            var projectDirectory = Path.Combine(temporary.Path, template.Id);
            var profile = string.Equals(template.Provider, "obsstudio", StringComparison.Ordinal)
                ? "32.x-windows-x64"
                : "1.0.4-stable";
            var created = await FoundryWorkspaceService.CreateAsync(new(
                projectDirectory,
                template.Name,
                $"com.example.{template.Id}",
                profile,
                template.Provider,
                template.Id,
                "Template Author",
                "Parameterized description"));

            Assert.True(created.IsSuccess, $"{template.Id}: {string.Join(Environment.NewLine, created.Diagnostics)}");
            Assert.Equal(template.Id, created.Value!.Manifest.Template!.Id);
            Assert.Equal(template.Revision, created.Value.Manifest.Template.Revision);
            Assert.Equal("Template Author", created.Value.Manifest.Template.Parameters["author"]);
            if (string.Equals(template.Provider, "obsstudio", StringComparison.Ordinal))
            {
                Assert.True(File.Exists(Path.Combine(projectDirectory, "src", "plugin.c")));
            }
            else
            {
                Assert.True(File.Exists(Path.Combine(projectDirectory, "streamerbot", "streamerbot.json")));
            }

            Assert.Equal("tests/foundry-tests.json", created.Value.Manifest.TestDefinition);
            var testDefinitionPath = Path.Combine(projectDirectory, "tests", "foundry-tests.json");
            Assert.True(File.Exists(testDefinitionPath));
            var testDefinition = await File.ReadAllTextAsync(testDefinitionPath);
            Assert.Contains($"\"provider\": \"{template.Provider}\"", testDefinition, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StreamerBotExtensionAndObsOutputTemplatesHaveExpectedShape()
    {
        using var temporary = TemporaryDirectory.Create();
        var streamer = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "streamer"),
            "Minimal Extension",
            "com.example.minimal-extension",
            "1.0.4-stable",
            "streamerbot",
            FoundryProjectTemplateService.StreamerBotExtension));
        var definition = await File.ReadAllTextAsync(Path.Combine(
            streamer.Value!.ProjectRoot,
            "streamerbot",
            "streamerbot.json"));
        Assert.Contains("\"kind\": \"test\"", definition, StringComparison.Ordinal);
        Assert.Contains("\"commands\": []", definition, StringComparison.Ordinal);

        var obs = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "obs"),
            "Output Plugin",
            "com.example.output-plugin",
            "32.x-windows-x64",
            "obsstudio",
            FoundryProjectTemplateService.ObsOutput));
        var source = await File.ReadAllTextAsync(Path.Combine(obs.Value!.ProjectRoot, "src", "plugin.c"));
        Assert.Equal(FoundryObsDesign.OutputTemplate, obs.Value.Manifest.ObsPlugin!.Design!.Template);
        Assert.Contains("obs_register_output", source, StringComparison.Ordinal);
        Assert.Contains("obs_output_end_data_capture", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReusableComponentsAreInstalledWithoutReplacingProjectFiles()
    {
        using var temporary = TemporaryDirectory.Create();
        var managed = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "managed"),
            "Managed Components",
            "com.example.managed-components",
            "1.0.4-stable"));
        var managedInstalled = await FoundryReusableComponentService.InstallAsync(
            managed.Value!,
            "creatorsforge.managed.arguments");

        Assert.True(managedInstalled.IsSuccess);
        Assert.Contains(managedInstalled.Value!.Manifest.Components, item => item.Id == "creatorsforge.managed.arguments");
        Assert.Contains("src/Components/FoundryArguments.cs", managedInstalled.Value.Manifest.ManagedBuild!.Sources);
        Assert.True(File.Exists(Path.Combine(managed.Value!.ProjectRoot, "src", "Components", "FoundryArguments.cs")));
        var duplicate = await FoundryReusableComponentService.InstallAsync(
            managedInstalled.Value,
            "creatorsforge.managed.arguments");
        Assert.False(duplicate.IsSuccess);
        Assert.Contains(duplicate.Diagnostics, item => item.Code == "CFW1203");

        var native = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "native"),
            "Native Components",
            "com.example.native-components",
            "32.x-windows-x64",
            "obsstudio"));
        var nativeInstalled = await FoundryReusableComponentService.InstallAsync(
            native.Value!,
            "creatorsforge.native.settings");

        Assert.True(nativeInstalled.IsSuccess);
        Assert.Contains("src/components/foundry_settings.c", nativeInstalled.Value!.Manifest.NativeBuild!.Sources);
        Assert.True(File.Exists(Path.Combine(native.Value!.ProjectRoot, "src", "components", "foundry_settings.h")));
    }

    [Fact]
    public async Task MultiProjectWorkspaceCreatesLoadsAddsAndActivatesProjects()
    {
        using var temporary = TemporaryDirectory.Create();
        var first = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "managed"),
            "Managed Project",
            "com.example.workspace-managed",
            "1.0.4-stable"));
        var second = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "native"),
            "Native Project",
            "com.example.workspace-native",
            "32.x-windows-x64",
            "obsstudio"));
        var workspacePath = Path.Combine(temporary.Path, "CreatorsForge.foundryworkspace");

        var created = await FoundryWorkspaceSetService.CreateAsync(
            workspacePath,
            "Creators Forge Workspace",
            [first.Value!.ProjectPath, second.Value!.ProjectPath]);

        Assert.True(created.IsSuccess);
        Assert.Equal(2, created.Value!.Projects.Count);
        Assert.Equal("Managed Project", created.Value.ActiveProject.Manifest.Name);
        Assert.All(created.Value.Manifest.Projects, path => Assert.DoesNotContain("..", path, StringComparison.Ordinal));

        var third = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "another"),
            "Another Project",
            "com.example.workspace-another",
            "1.0.4-stable"));
        var added = await FoundryWorkspaceSetService.AddProjectAsync(created.Value, third.Value!.ProjectPath);
        Assert.True(added.IsSuccess);
        Assert.Equal(3, added.Value!.Projects.Count);

        var activated = FoundryWorkspaceSetService.Activate(added.Value, second.Value.ProjectPath);
        Assert.Equal(second.Value.ProjectPath, activated.ActiveProject.ProjectPath);
    }

    [Fact]
    public async Task MultiProjectWorkspaceRejectsParentTraversal()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "unsafe.foundryworkspace");
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 1,
              "name": "Unsafe",
              "projects": ["../outside.foundryproj"]
            }
            """);

        var result = await FoundryWorkspaceSetService.LoadAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFW1313");
    }

    [Fact]
    public async Task ProjectTemplateExportsAndImportsParameterizedSourceProject()
    {
        using var temporary = TemporaryDirectory.Create();
        var original = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "original"),
            "Original Extension",
            "com.example.original-extension",
            "1.0.4-stable"));
        var templatePath = Path.Combine(temporary.Path, "extension.foundrytemplate");

        var exportDiagnostics = await FoundryTemplateInterchangeService.ExportAsync(original.Value!, templatePath);
        var imported = await FoundryTemplateInterchangeService.ImportAsync(new(
            templatePath,
            Path.Combine(temporary.Path, "imported"),
            "Imported Extension",
            "com.example.imported-extension",
            "1.0.5-beta.1"));

        Assert.Empty(exportDiagnostics);
        Assert.True(imported.IsSuccess, string.Join(Environment.NewLine, imported.Diagnostics));
        Assert.Equal("Imported Extension", imported.Value!.Manifest.Name);
        Assert.Equal("com.example.imported-extension", imported.Value.Manifest.Id);
        Assert.Equal("1.0.5-beta.1", imported.Value.Manifest.Target!.Profile);
        Assert.Equal("com.example.original-extension.template-v1", imported.Value.Manifest.Template!.Id);
        var source = await File.ReadAllTextAsync(Path.Combine(imported.Value.ProjectRoot, "src", "EntryPoint.cs"));
        Assert.Contains("ImportedExtension", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginalExtension", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchemaZeroMigrationCreatesBackupAndPreservesUnknownRootData()
    {
        using var temporary = TemporaryDirectory.Create();
        var created = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "legacy"),
            "Legacy Extension",
            "com.example.legacy-migration",
            "1.0.4-stable"));
        var projectPath = created.Value!.ProjectPath;
        var json = JsonNode.Parse(await File.ReadAllTextAsync(projectPath))!.AsObject();
        json.Remove("schemaVersion");
        json.Remove("template");
        json.Remove("components");
        json["legacyNote"] = "preserve me";
        var legacyText = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(projectPath, legacyText);

        var inspection = await FoundryProjectMigrationService.InspectAsync(projectPath);
        var migrated = await FoundryProjectMigrationService.MigrateAsync(projectPath);

        Assert.True(inspection.IsSuccess);
        Assert.True(inspection.Plan!.IsRequired);
        Assert.True(migrated.IsSuccess, string.Join(Environment.NewLine, migrated.Diagnostics));
        Assert.Equal(1, migrated.Workspace!.Manifest.SchemaVersion);
        Assert.NotNull(migrated.Workspace.Manifest.Template);
        Assert.Equal("preserve me", migrated.Workspace.Manifest.AdditionalProperties!["legacyNote"].GetString());
        Assert.Equal(legacyText, await File.ReadAllTextAsync(inspection.Plan.BackupPath));
    }

    [Fact]
    public async Task MigrationRefusesToOverwriteDifferentBackup()
    {
        using var temporary = TemporaryDirectory.Create();
        var created = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "legacy"),
            "Legacy Backup",
            "com.example.legacy-backup",
            "1.0.4-stable"));
        var projectPath = created.Value!.ProjectPath;
        var json = JsonNode.Parse(await File.ReadAllTextAsync(projectPath))!.AsObject();
        json.Remove("schemaVersion");
        await File.WriteAllTextAsync(projectPath, json.ToJsonString());
        await File.WriteAllTextAsync(projectPath + ".schema0.backup", "different");

        var result = await FoundryProjectMigrationService.MigrateAsync(projectPath);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFW1507");
        Assert.False(JsonNode.Parse(await File.ReadAllTextAsync(projectPath))!.AsObject().ContainsKey("schemaVersion"));
    }

    [Fact]
    public async Task ObsDesignerSavePersistsManifestAndGeneratedSource()
    {
        using var temporary = TemporaryDirectory.Create();
        var created = await FoundryWorkspaceService.CreateAsync(
            new(
                Path.Combine(temporary.Path, "DesignerPlugin"),
                "Designer Plugin",
                "com.example.designer-plugin",
                "32.x-windows-x64",
                "obsstudio"));
        var workspace = created.Value!;
        var design = workspace.Manifest.ObsPlugin!.Design! with
        {
            Template = FoundryObsDesign.VideoInputTemplate,
            ComponentId = "com.example.designer-input",
            ComponentName = "Designer Input",
        };
        var generated = ObsPluginTemplateService.Generate(
            workspace.Manifest.ObsPlugin,
            design);

        var saved = await FoundryWorkspaceService.SaveObsPluginDesignAsync(
            workspace,
            workspace.Manifest.ObsPlugin,
            design,
            generated.Source!);

        Assert.True(saved.IsSuccess);
        Assert.Equal(
            FoundryObsDesign.VideoInputTemplate,
            saved.Value!.Manifest.ObsPlugin!.Design!.Template);
        Assert.Contains(
            "OBS_SOURCE_TYPE_INPUT",
            await File.ReadAllTextAsync(Path.Combine(workspace.ProjectRoot, "src", "plugin.c")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyProjectCanEnableStreamerBotPackaging()
    {
        using var temporary = TemporaryDirectory.Create();
        var projectDirectory = Path.Combine(temporary.Path, "LegacyExtension");
        var created = await FoundryWorkspaceService.CreateAsync(
            new(
                projectDirectory,
                "Legacy Extension",
                "com.example.legacy-extension",
                "1.0.4-stable"),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var legacyManifest = created.Value!.Manifest with
        {
            TargetDefinition = null,
            Outputs =
            [
                FoundryOutputKinds.ManagedLibrary,
                FoundryOutputKinds.CphInlineBridge,
            ],
        };
        await File.WriteAllTextAsync(
            created.Value.ProjectPath,
            JsonSerializer.Serialize(
                legacyManifest,
                ManifestOptions));
        Directory.Delete(
            Path.Combine(projectDirectory, "streamerbot"),
            recursive: true);
        var legacy = await FoundryWorkspaceService.OpenAsync(
            created.Value.ProjectPath);

        var upgraded =
            await FoundryWorkspaceService.EnableStreamerBotPackagingAsync(
                legacy.Value!);

        Assert.True(upgraded.IsSuccess);
        Assert.Equal(
            "streamerbot/streamerbot.json",
            upgraded.Value!.Manifest.TargetDefinition);
        Assert.Contains(
            FoundryOutputKinds.StreamerBotPackage,
            upgraded.Value.Manifest.Outputs);
        Assert.True(File.Exists(
            Path.Combine(
                projectDirectory,
                "streamerbot",
                "streamerbot.json")));
    }

    [Fact]
    public async Task DocumentServiceRejectsPathsOutsideWorkspace()
    {
        using var temporary = TemporaryDirectory.Create();
        var outsidePath = Path.Combine(
            Path.GetDirectoryName(temporary.Path)!,
            $"{Guid.NewGuid():N}.txt");

        var result = await WorkspaceDocumentService.SaveAsync(
            temporary.Path,
            outsidePath,
            "outside",
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("CFW1001", Assert.Single(result.Diagnostics).Code);
        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task RecentProjectsRoundTripAndCorruptionDoesNotBlockStartup()
    {
        using var temporary = TemporaryDirectory.Create();
        var statePath = Path.Combine(temporary.Path, "recent.json");
        var store = new RecentProjectsStore(statePath);
        var firstPath = Path.Combine(temporary.Path, "One.foundryproj");
        var secondPath = Path.Combine(temporary.Path, "Two.foundryproj");

        await store.SaveOpenedProjectAsync(firstPath, "One", CancellationToken.None);
        await store.SaveOpenedProjectAsync(secondPath, "Two", CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(["Two", "One"], loaded.Value.Select(entry => entry.Name));

        await File.WriteAllTextAsync(statePath, "{ invalid", CancellationToken.None);
        var corrupted = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(corrupted.Value);
        Assert.Equal("CFW2001", Assert.Single(corrupted.Diagnostics).Code);
    }

    [Fact]
    public async Task SettingsRoundTripClampsUnsafeLayoutAndFallsBackWhenBroken()
    {
        using var temporary = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        var store = new FoundrySettingsStore(settingsPath);
        var settings = new FoundryUserSettings(
            temporary.Path,
            1,
            new ShellLayout(WindowWidth: 100, BottomPanelHeight: 5000)) with
        {
            UpdateChannel = FoundryUpdateChannel.Prerelease,
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(10, loaded.Value.AutosaveSeconds);
        Assert.Equal(900, loaded.Value.Layout.WindowWidth);
        Assert.Equal(600, loaded.Value.Layout.BottomPanelHeight);
        Assert.Equal(FoundryUpdateChannel.Prerelease, loaded.Value.UpdateChannel);

        await File.WriteAllTextAsync(settingsPath, "[]", CancellationToken.None);
        var broken = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(
            FoundryUserSettings.CreateDefault().AutosaveSeconds,
            broken.Value.AutosaveSeconds);
        Assert.Equal("CFW2101", Assert.Single(broken.Diagnostics).Code);
    }

    [Fact]
    public async Task RecoveryRoundTripAndDelete()
    {
        using var temporary = TemporaryDirectory.Create();
        var documentPath = Path.Combine(temporary.Path, "EntryPoint.cs");
        var store = new RecoveryStore(Path.Combine(temporary.Path, "recovery"));

        await store.WriteAsync(documentPath, "unsaved text", CancellationToken.None);
        var recovered = await store.ReadAsync(documentPath, CancellationToken.None);

        Assert.Equal("unsaved text", recovered?.Text);

        await store.DeleteAsync(documentPath);

        Assert.Null(await store.ReadAsync(documentPath, CancellationToken.None));
    }

    [Fact]
    public async Task PublishingSettingsAndSemanticVersionBumpsRoundTrip()
    {
        using var temporary = TemporaryDirectory.Create();
        var created = await FoundryWorkspaceService.CreateAsync(new(
            Path.Combine(temporary.Path, "Publishing"),
            "Publishing Test",
            "com.example.publishing-test",
            "1.0.4-stable"));
        Assert.True(created.IsSuccess);

        var saved = await FoundryPublishingService.SaveReleaseSettingsAsync(
            created.Value!,
            new FoundryPublishing
            {
                PackageName = "com.example.publishing-test",
                Summary = "Release metadata test.",
                Authors = ["Example Author"],
                Tags = ["Tools", "tools", "Streaming"],
            },
            "1.2.3");
        Assert.True(saved.IsSuccess);
        Assert.Equal("1.2.3", saved.Value!.Manifest.Version);
        Assert.Equal(["Streaming", "Tools"], saved.Value.Manifest.Publishing!.Tags);

        var bumped = await FoundryPublishingService.SetVersionAsync(saved.Value, "minor");
        Assert.True(bumped.IsSuccess);
        Assert.Equal("1.3.0", bumped.Value!.Manifest.Version);
        Assert.Equal("Release metadata test.", bumped.Value.Manifest.Publishing!.Summary);
    }

    [Fact]
    public async Task LocalUpdateManifestAndPackageWorkWithNetworkDisabled()
    {
        using var temporary = TemporaryDirectory.Create();
        var package = Path.Combine(temporary.Path, "foundry.zip");
        await File.WriteAllTextAsync(package, "verified update package");
        var bytes = await File.ReadAllBytesAsync(package);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var manifestPath = Path.Combine(temporary.Path, "foundry-update.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "2.0.0",
            packageUrl = "foundry.zip",
            sha256 = hash,
            size = bytes.Length,
            publishedAtUtc = DateTimeOffset.UtcNow,
        }, ManifestOptions));

        var check = await FoundryUpdateService.CheckAsync(
            manifestPath,
            "1.0.0",
            allowNetworkAccess: false,
            FoundryUpdateChannel.Prerelease);
        Assert.True(check.IsSuccess);
        Assert.True(check.IsUpdateAvailable);
        Assert.Equal(package, check.Manifest!.PackageUrl);
        var staged = await FoundryUpdateService.StageAsync(check.Manifest, Path.Combine(temporary.Path, "staged"), allowNetworkAccess: false);
        Assert.Empty(staged.Diagnostics);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(staged.PackagePath!));
    }

    [Fact]
    public async Task NativeUpdateStagesAsExeAndCreatesElevatedInstallerLaunch()
    {
        using var temporary = TemporaryDirectory.Create();
        var package = Path.Combine(temporary.Path, "CreatorsForge-Foundry-2.0.0-Update.exe");
        await File.WriteAllTextAsync(package, "verified native updater");
        var bytes = await File.ReadAllBytesAsync(package);
        var manifest = new FoundryUpdateManifest(
            1,
            "2.0.0",
            package,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            bytes.Length,
            DateTimeOffset.UtcNow);

        var staged = await FoundryUpdateService.StageAsync(
            manifest,
            Path.Combine(temporary.Path, "staged"),
            allowNetworkAccess: false);

        Assert.Empty(staged.Diagnostics);
        Assert.EndsWith("-Update.exe", staged.PackagePath, StringComparison.OrdinalIgnoreCase);
        var startInfo = FoundryUpdateService.CreateInstallerStartInfo(staged.PackagePath!);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Contains("/CLOSEAPPLICATIONS", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("/NORESTART", startInfo.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultSettingsUseOfficialGitHubReleaseManifest()
    {
        var settings = FoundryUserSettings.CreateDefault();

        Assert.Equal(
            "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/latest/download/foundry-update.json",
            settings.UpdateManifestLocation);
        Assert.False(settings.AllowNetworkAccess);
        Assert.Equal(FoundryUpdateChannel.Stable, settings.UpdateChannel);
    }

    [Fact]
    public async Task PrereleaseChannelStillRequiresExplicitNetworkAccess()
    {
        var result = await FoundryUpdateService.CheckAsync(
            FoundryUserSettings.DefaultUpdateManifestLocation,
            "0.19.0-alpha.4",
            allowNetworkAccess: false,
            FoundryUpdateChannel.Prerelease);

        Assert.False(result.IsSuccess);
        Assert.Equal("CFU1002", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void PrereleaseChannelSelectsHighestPublishedGitHubManifestAndExcludesDrafts()
    {
        const string releasesJson = """
            [
              {
                "tag_name": "v0.21.0-alpha.1",
                "draft": true,
                "prerelease": true,
                "published_at": "2026-08-04T12:00:00Z",
                "assets": [
                  {
                    "name": "foundry-update.json",
                    "state": "uploaded",
                    "browser_download_url": "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/download/v0.21.0-alpha.1/foundry-update.json"
                  }
                ]
              },
              {
                "tag_name": "v0.20.0-alpha.2",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-08-03T12:00:00Z",
                "assets": [
                  {
                    "name": "foundry-update.json",
                    "state": "uploaded",
                    "browser_download_url": "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/download/v0.20.0-alpha.2/foundry-update.json"
                  }
                ]
              },
              {
                "tag_name": "v0.20.0-alpha.1",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-08-04T12:00:00Z",
                "assets": [
                  {
                    "name": "foundry-update.json",
                    "state": "uploaded",
                    "browser_download_url": "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/download/v0.20.0-alpha.1/foundry-update.json"
                  }
                ]
              },
              {
                "tag_name": "v0.22.0-alpha.1",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-08-04T12:00:00Z",
                "assets": [
                  {
                    "name": "foundry-update.json",
                    "state": "uploaded",
                    "browser_download_url": "https://example.invalid/foundry-update.json"
                  }
                ]
              }
            ]
            """;

        var location = FoundryUpdateService.SelectOfficialPrereleaseManifestLocation(releasesJson);

        Assert.Equal(
            "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/download/v0.20.0-alpha.2/foundry-update.json",
            location);
    }

    [Fact]
    public void PrereleaseChannelIncludesAStableReleaseAtTheSameCoreVersion()
    {
        const string releasesJson = """
            [
              {
                "tag_name": "v0.20.0-alpha.9",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-08-03T12:00:00Z",
                "assets": [{
                  "name": "foundry-update.json",
                  "state": "uploaded",
                  "browser_download_url": "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/download/v0.20.0-alpha.9/foundry-update.json"
                }]
              },
              {
                "tag_name": "v0.20.0",
                "draft": false,
                "prerelease": false,
                "published_at": "2026-08-02T12:00:00Z",
                "assets": [{
                  "name": "foundry-update.json",
                  "state": "uploaded",
                  "browser_download_url": "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/download/v0.20.0/foundry-update.json"
                }]
              }
            ]
            """;

        var location = FoundryUpdateService.SelectOfficialPrereleaseManifestLocation(releasesJson);

        Assert.Equal(
            "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/download/v0.20.0/foundry-update.json",
            location);
    }

    [Fact]
    public async Task UpdateServiceBlocksNetworkByDefaultAndRejectsModifiedPackage()
    {
        var blocked = await FoundryUpdateService.CheckAsync("https://example.invalid/foundry-update.json", "1.0.0", allowNetworkAccess: false);
        Assert.False(blocked.IsSuccess);
        Assert.Equal("CFU1002", Assert.Single(blocked.Diagnostics).Code);

        using var temporary = TemporaryDirectory.Create();
        var package = Path.Combine(temporary.Path, "bad.zip");
        await File.WriteAllTextAsync(package, "modified");
        var staged = await FoundryUpdateService.StageAsync(new(1, "2.0.0", package, new string('0', 64), 8, DateTimeOffset.UtcNow),
            Path.Combine(temporary.Path, "staged"), allowNetworkAccess: false);
        Assert.Null(staged.PackagePath);
        Assert.Equal("CFU1012", Assert.Single(staged.Diagnostics).Code);
    }

    [Theory]
    [InlineData("0.15.0-alpha.2", "0.15.0-alpha.1", true)]
    [InlineData("0.15.0", "0.15.0-alpha.9", true)]
    [InlineData("0.15.0-alpha.1", "0.15.0-alpha.1", false)]
    [InlineData("0.15.0-alpha.1", "0.15.0-alpha.2", false)]
    public async Task UpdateServiceOrdersPrivateAlphaVersions(string candidate, string current, bool expected)
    {
        using var temporary = TemporaryDirectory.Create();
        var package = Path.Combine(temporary.Path, "foundry.zip");
        await File.WriteAllTextAsync(package, "alpha package");
        var bytes = await File.ReadAllBytesAsync(package);
        var manifestPath = Path.Combine(temporary.Path, "foundry-update.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = candidate,
            packageUrl = "foundry.zip",
            sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            size = bytes.Length,
            publishedAtUtc = DateTimeOffset.UtcNow,
        }, ManifestOptions));

        var result = await FoundryUpdateService.CheckAsync(manifestPath, current, false);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.IsUpdateAvailable);
    }

    [Fact]
    public async Task FailureReportsAndDiagnosticBundlesRemainLocalAndRedactPaths()
    {
        using var temporary = TemporaryDirectory.Create();
        var service = new FoundryFailureReportService(Path.Combine(temporary.Path, "failures"));
        var report = await service.WriteAsync(new InvalidOperationException("fixture failure"), "test");
        Assert.True(File.Exists(report));
        Assert.Single(service.ListReports());
        var bundle = Path.Combine(temporary.Path, "diagnostics.zip");
        await service.CreateBundleAsync(bundle, FoundryUserSettings.CreateDefault(), includePaths: false);
        using var archive = System.IO.Compression.ZipFile.OpenRead(bundle);
        Assert.Contains(archive.Entries, item => item.FullName.StartsWith("failures/", StringComparison.Ordinal));
        var summary = archive.GetEntry("system-summary.json")!;
        using var reader = new StreamReader(summary.Open());
        Assert.Contains("[redacted]", await reader.ReadToEndAsync(), StringComparison.Ordinal);
        Assert.NotNull(archive.GetEntry("bundle-manifest.json"));
        Assert.NotNull(archive.GetEntry("issue-report.md"));
        Assert.DoesNotContain(archive.Entries, item => item.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LargeProjectTreeRemainsBoundedAndResponsive()
    {
        using var temporary = TemporaryDirectory.Create();
        var created = await FoundryWorkspaceService.CreateAsync(new(Path.Combine(temporary.Path, "Large"), "Large Project", "com.example.large", "1.0.4-stable"));
        Assert.True(created.IsSuccess);
        var generated = Path.Combine(created.Value!.ProjectRoot, "assets");
        Directory.CreateDirectory(generated);
        for (var index = 0; index < 2_000; index++) await File.WriteAllTextAsync(Path.Combine(generated, $"asset-{index:D4}.txt"), index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var reopened = await FoundryWorkspaceService.OpenAsync(created.Value.ProjectPath);
        timer.Stop();
        Assert.True(reopened.IsSuccess);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"Large project opened in {timer.Elapsed}.");
        Assert.InRange(Count(reopened.Value!.ProjectTree), 2_000, 10_000);

        static int Count(IReadOnlyList<ProjectTreeNode> nodes) => nodes.Count + nodes.Sum(item => Count(item.Children));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CreatorsForge.Foundry.Workspaces.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
