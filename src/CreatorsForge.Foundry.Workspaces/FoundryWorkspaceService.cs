using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public static class FoundryWorkspaceService
{
    private const int MaximumTreeEntries = 10_000;
    private const int MaximumTreeDepth = 32;

    private static readonly HashSet<string> IgnoredDirectoryNames =
        new HashSet<string>(
            [".git", ".vs", "bin", "obj", "build", "TestResults"],
            StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> OpenAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var loadResult = await FoundryProjectLoader.LoadAsync(
            projectPath,
            cancellationToken).ConfigureAwait(false);
        var diagnostics = new List<FoundryDiagnostic>(loadResult.Diagnostics);

        if (loadResult.Manifest is null || loadResult.ProjectPath is null)
        {
            return new(null, diagnostics);
        }

        diagnostics.AddRange(
            FoundryProjectValidator.Validate(
                loadResult.Manifest,
                loadResult.ProjectPath));
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new(null, diagnostics);
        }

        var projectRoot = Path.GetDirectoryName(loadResult.ProjectPath)!;
        IReadOnlyList<ProjectTreeNode> tree;

        try
        {
            tree = await Task.Run(
                () => BuildTree(projectRoot, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new(
                "CFW0001",
                FoundryDiagnosticSeverity.Warning,
                $"The project opened, but its file tree could not be fully read: {exception.Message}",
                new FoundryDiagnosticLocation(loadResult.ProjectPath),
                "Check access to the project directory and refresh the workspace."));
            tree = [];
        }

        return new(
            new(
                loadResult.ProjectPath,
                projectRoot,
                loadResult.Manifest,
                tree),
            diagnostics);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> CreateAsync(
        FoundryProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string projectDirectory;
        try
        {
            projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                "CFW0101",
                $"The project directory is invalid: {exception.Message}",
                request.ProjectDirectory);
        }

        if (Directory.Exists(projectDirectory) &&
            Directory.EnumerateFileSystemEntries(projectDirectory).Any())
        {
            return Failure(
                "CFW0102",
                "The project directory must be empty.",
                projectDirectory);
        }

        var namespaceName = CreateIdentifier(request.Name);
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return Failure(
                "CFW0103",
                "The project name must contain at least one letter or digit.",
                projectDirectory);
        }

        var isObsProject = string.Equals(request.TargetProvider, "obsstudio", StringComparison.Ordinal);
        var template = FoundryProjectTemplateService.Find(
            request.TargetProvider,
            request.TemplateId);
        if (template is null)
        {
            return Failure(
                "CFW0105",
                "The selected project template is not available for this provider.",
                projectDirectory);
        }
        var isStreamerBotSourcePackage = string.Equals(
            template.Id,
            FoundryProjectTemplateService.StreamerBotExtension,
            StringComparison.Ordinal);

        var author = string.IsNullOrWhiteSpace(request.Author)
            ? "Creator"
            : request.Author.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? template.Description
            : request.Description.Trim();
        var publishing = new FoundryPublishing
        {
            PackageName = request.Id.Trim(),
            Summary = description,
            Authors = [author],
            LicenseFile = "LICENSE.txt",
            ChangelogFile = "CHANGELOG.md",
        };
        var templateReference = new FoundryProjectTemplateReference
        {
            Id = template.Id,
            Revision = template.Revision,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["author"] = author,
                ["description"] = description,
            },
        };
        var manifest = isObsProject
            ? new FoundryProjectManifest
            {
                Name = request.Name.Trim(),
                Id = request.Id.Trim(),
                Version = "0.1.0",
                Target = new FoundryTarget
                {
                    Provider = "obsstudio",
                    Profile = request.TargetProfile.Trim(),
                },
                Template = templateReference,
                Publishing = publishing,
                NativeBuild = new FoundryNativeBuild
                {
                    Sources = ["src/plugin.c"],
                },
                ObsPlugin = new FoundryObsPlugin
                {
                    Contract = FoundryObsPlugin.SdkContract,
                    ModuleName = CreateObsModuleName(request.Id),
                    DisplayName = request.Name.Trim(),
                    Author = author,
                    Description = description,
                    ApiVersion = FoundryObsPlugin.SupportedSdkVersion,
                    SdkVersion = FoundryObsPlugin.SupportedSdkVersion,
                    Design = new FoundryObsDesign
                    {
                        Template = FoundryProjectTemplateService.GetObsDesignTemplate(template.Id)!,
                        Source = "src/plugin.c",
                        ComponentId = $"{request.Id.Trim()}.filter",
                        ComponentName = request.Name.Trim(),
                    },
                },
                TestDefinition = "tests/foundry-tests.json",
                Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
            }
            : new FoundryProjectManifest
            {
            Name = request.Name.Trim(),
            Id = request.Id.Trim(),
            Version = "0.1.0",
            Target = new FoundryTarget
            {
                Provider = "streamerbot",
                Profile = request.TargetProfile.Trim(),
            },
            Template = templateReference,
            Publishing = publishing,
            Features = new FoundryFeatures
            {
                MockRuntime = true,
            },
            ManagedBuild = isStreamerBotSourcePackage ? null : new FoundryManagedBuild
            {
                AssemblyName = $"CreatorsForge.Extensions.{namespaceName}",
                Sources = ["src/EntryPoint.cs"],
            },
            CphInlineBridge = isStreamerBotSourcePackage ? null : new FoundryCphInlineBridge
            {
                Contract = FoundryCphInlineBridge.SupportedContract,
                EntryType = $"CreatorsForge.Extensions.{namespaceName}.EntryPoint",
                EntryMethod = "Execute",
            },
            TargetDefinition = "streamerbot/streamerbot.json",
            TestDefinition = isStreamerBotSourcePackage ? null : "tests/foundry-tests.json",
            Outputs = isStreamerBotSourcePackage
                ? [FoundryOutputKinds.StreamerBotPackage]
                :
                [
                    FoundryOutputKinds.ManagedLibrary,
                    FoundryOutputKinds.CphInlineBridge,
                    FoundryOutputKinds.StreamerBotPackage,
                ],
            };
        var projectPath = Path.Combine(
            projectDirectory,
            $"{namespaceName}.foundryproj");
        var diagnostics = FoundryProjectValidator.Validate(manifest, projectPath);
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new(null, diagnostics);
        }

        var directoryCreated = !Directory.Exists(projectDirectory);
        try
        {
            Directory.CreateDirectory(Path.Combine(projectDirectory, "src"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "tests"));
            var manifestJson = JsonSerializer.Serialize(
                manifest,
                ManifestSerializerOptions);
            await AtomicFile.WriteTextAsync(
                projectPath,
                $"{manifestJson}\n",
                cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteTextAsync(
                Path.Combine(projectDirectory, "LICENSE.txt"),
                CreateMitLicense(author),
                cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteTextAsync(
                Path.Combine(projectDirectory, "CHANGELOG.md"),
                CreateInitialChangelog(manifest.Name, manifest.Version),
                cancellationToken).ConfigureAwait(false);
            if (isObsProject)
            {
                var generated = ObsPluginTemplateService.Generate(
                    manifest.ObsPlugin!,
                    manifest.ObsPlugin!.Design!);
                if (!generated.IsSuccess)
                {
                    throw new InvalidDataException(string.Join(" ", generated.Errors));
                }

                await AtomicFile.WriteTextAsync(
                    Path.Combine(projectDirectory, "src", "plugin.c"),
                    generated.Source!,
                    cancellationToken).ConfigureAwait(false);
                await AtomicFile.WriteTextAsync(
                    Path.Combine(projectDirectory, "tests", "foundry-tests.json"),
                    CreateObsTestDefinitionJson(manifest, template.Id),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!isStreamerBotSourcePackage)
                {
                    await AtomicFile.WriteTextAsync(
                        Path.Combine(projectDirectory, "src", "EntryPoint.cs"),
                        CreateEntryPointSource(namespaceName),
                        cancellationToken).ConfigureAwait(false);
                }
                await AtomicFile.WriteTextAsync(
                    Path.Combine(projectDirectory, "streamerbot", "streamerbot.json"),
                    CreateStreamerBotDefinitionJson(
                        manifest,
                        template.Id,
                        author,
                    description),
                    cancellationToken).ConfigureAwait(false);
                if (!isStreamerBotSourcePackage)
                {
                    await AtomicFile.WriteTextAsync(
                        Path.Combine(projectDirectory, "tests", "foundry-tests.json"),
                        CreateStreamerBotTestDefinitionJson(namespaceName),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (directoryCreated)
            {
                TryDeleteNewProjectDirectory(projectDirectory);
            }

            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (directoryCreated)
            {
                TryDeleteNewProjectDirectory(projectDirectory);
            }

            return Failure(
                "CFW0104",
                $"The project could not be created: {exception.Message}",
                projectDirectory);
        }

        return await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>>
        SaveObsPluginDesignAsync(
            FoundryWorkspace workspace,
            FoundryObsPlugin plugin,
            FoundryObsDesign design,
            string generatedSource,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(generatedSource);

        var updatedPlugin = plugin with { Design = design };
        var updatedManifest = workspace.Manifest with { ObsPlugin = updatedPlugin };
        var diagnostics = FoundryProjectValidator.Validate(
            updatedManifest,
            workspace.ProjectPath).ToList();
        var templateErrors = ObsPluginTemplateService.Validate(updatedPlugin, design);
        diagnostics.AddRange(templateErrors.Select(error => new FoundryDiagnostic(
            "CFW0301",
            FoundryDiagnosticSeverity.Error,
            error,
            new FoundryDiagnosticLocation(workspace.ProjectPath, "$.obsPlugin.design"))));
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new(null, diagnostics);
        }

        var sourcePath = Path.GetFullPath(Path.Combine(
            workspace.ProjectRoot,
            design.Source.Replace('/', Path.DirectorySeparatorChar)));
        var projectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspace.ProjectRoot));
        if (!sourcePath.StartsWith(
            $"{projectRoot}{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "CFW0302",
                "The generated source path must remain inside the project.",
                sourcePath);
        }

        string? originalSource = null;
        string? originalManifest = null;
        var sourceSaved = false;
        try
        {
            originalSource = await File.ReadAllTextAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
            originalManifest = await File.ReadAllTextAsync(
                workspace.ProjectPath,
                cancellationToken).ConfigureAwait(false);
            var manifestJson = JsonSerializer.Serialize(
                updatedManifest,
                ManifestSerializerOptions);
            await AtomicFile.WriteTextAsync(
                sourcePath,
                generatedSource,
                cancellationToken).ConfigureAwait(false);
            sourceSaved = true;
            await AtomicFile.WriteTextAsync(
                workspace.ProjectPath,
                $"{manifestJson}\n",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (sourceSaved && originalSource is not null && originalManifest is not null)
            {
                try
                {
                    await AtomicFile.WriteTextAsync(
                        sourcePath,
                        originalSource,
                        CancellationToken.None).ConfigureAwait(false);
                    await AtomicFile.WriteTextAsync(
                        workspace.ProjectPath,
                        originalManifest,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackException) when (
                    rollbackException is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new(
                        "CFW0304",
                        FoundryDiagnosticSeverity.Error,
                        $"The failed OBS design save could not be fully rolled back: {rollbackException.Message}",
                        new FoundryDiagnosticLocation(workspace.ProjectPath),
                        "Restore the project manifest and generated source from version control or backup."));
                }
            }

            diagnostics.Add(new(
                "CFW0303",
                FoundryDiagnosticSeverity.Error,
                $"The OBS design could not be saved: {exception.Message}",
                new FoundryDiagnosticLocation(workspace.ProjectPath)));
            return new(null, diagnostics);
        }

        return await OpenAsync(workspace.ProjectPath, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>>
        SavePreviewAsync(
            FoundryWorkspace workspace,
            FoundryPreview? preview,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var current = await FoundryProjectLoader.LoadAsync(
            workspace.ProjectPath,
            cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess)
        {
            return new(null, current.Diagnostics);
        }
        var updatedManifest = current.Manifest! with { Preview = preview };
        var diagnostics = FoundryProjectValidator.Validate(
            updatedManifest,
            workspace.ProjectPath);
        if (diagnostics.Any(item => item.IsError))
        {
            return new(null, diagnostics);
        }

        try
        {
            var manifestJson = JsonSerializer.Serialize(
                updatedManifest,
                ManifestSerializerOptions);
            await AtomicFile.WriteTextAsync(
                workspace.ProjectPath,
                $"{manifestJson}\n",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "CFW2306",
                $"Preview settings could not be saved: {exception.Message}",
                workspace.ProjectPath);
        }

        return await OpenAsync(workspace.ProjectPath, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>>
        EnableStreamerBotPackagingAsync(
            FoundryWorkspace workspace,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!string.Equals(
                workspace.Manifest.Target?.Provider,
                "streamerbot",
                StringComparison.Ordinal))
        {
            return Failure(
                "CFW0201",
                "Only Streamer.bot projects can enable Streamer.bot package output.",
                workspace.ProjectPath);
        }

        var targetDefinition = string.IsNullOrWhiteSpace(
            workspace.Manifest.TargetDefinition)
            ? "streamerbot/streamerbot.json"
            : workspace.Manifest.TargetDefinition;
        var outputs = workspace.Manifest.Outputs
            .Append(FoundryOutputKinds.ManagedLibrary)
            .Append(FoundryOutputKinds.CphInlineBridge)
            .Append(FoundryOutputKinds.StreamerBotPackage)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var updatedManifest = workspace.Manifest with
        {
            TargetDefinition = targetDefinition,
            Outputs = outputs,
        };
        var diagnostics = FoundryProjectValidator.Validate(
            updatedManifest,
            workspace.ProjectPath);
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new(null, diagnostics);
        }

        var definitionPath = Path.GetFullPath(
            Path.Combine(
                workspace.ProjectRoot,
                targetDefinition.Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            if (!File.Exists(definitionPath))
            {
                await AtomicFile.WriteTextAsync(
                    definitionPath,
                    CreateStreamerBotDefinitionJson(updatedManifest),
                    cancellationToken).ConfigureAwait(false);
            }

            var manifestJson = JsonSerializer.Serialize(
                updatedManifest,
                ManifestSerializerOptions);
            await AtomicFile.WriteTextAsync(
                workspace.ProjectPath,
                $"{manifestJson}\n",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "CFW0202",
                $"Streamer.bot packaging could not be enabled: {exception.Message}",
                workspace.ProjectPath);
        }

        return await OpenAsync(workspace.ProjectPath, cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<ProjectTreeNode> BuildTree(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var entryCount = 0;
        return BuildDirectory(projectRoot, projectRoot, 0, ref entryCount, cancellationToken);
    }

    private static List<ProjectTreeNode> BuildDirectory(
        string projectRoot,
        string directory,
        int depth,
        ref int entryCount,
        CancellationToken cancellationToken)
    {
        if (depth > MaximumTreeDepth || entryCount >= MaximumTreeEntries)
        {
            return [];
        }

        var nodes = new List<ProjectTreeNode>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory)
                     .OrderBy(
                         path => !Directory.Exists(path),
                         Comparer<bool>.Default)
                     .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entryCount > MaximumTreeEntries)
            {
                break;
            }

            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var name = Path.GetFileName(entry);
            if (isDirectory && IgnoredDirectoryNames.Contains(name))
            {
                continue;
            }

            var children = isDirectory
                ? BuildDirectory(
                    projectRoot,
                    entry,
                    depth + 1,
                    ref entryCount,
                    cancellationToken)
                : [];
            nodes.Add(new(
                name,
                entry,
                Path.GetRelativePath(projectRoot, entry).Replace('\\', '/'),
                isDirectory,
                children));
        }

        return nodes;
    }

    private static WorkspaceOperationResult<FoundryWorkspace> Failure(
        string code,
        string message,
        string path)
    {
        var diagnostic = new FoundryDiagnostic(
            code,
            FoundryDiagnosticSeverity.Error,
            message,
            new FoundryDiagnosticLocation(path));
        return new(null, [diagnostic]);
    }

    private static string CreateIdentifier(string name)
    {
        var builder = new StringBuilder();
        var capitalizeNext = true;
        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character))
            {
                capitalizeNext = true;
                continue;
            }

            builder.Append(capitalizeNext
                ? char.ToUpperInvariant(character)
                : character);
            capitalizeNext = false;
        }

        if (builder.Length > 0 && char.IsDigit(builder[0]))
        {
            builder.Insert(0, "Extension");
        }

        return builder.ToString();
    }

    private static string CreateMitLicense(string author)
    {
        var copyrightOwner = author
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return $$"""
            MIT License

            Copyright (c) {{DateTimeOffset.UtcNow.Year}} {{copyrightOwner}}

            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            SOFTWARE.

            """;
    }

    private static string CreateInitialChangelog(string projectName, string version) => $$"""
        # Changelog

        All notable changes to {{projectName}} will be documented in this file.

        ## {{version}}

        - Initial project created with Creators Forge Foundry.

        """;

    private static string CreateEntryPointSource(string namespaceName) => $$"""
        using System;
        using System.Collections.Generic;

        namespace CreatorsForge.Extensions.{{namespaceName}}
        {
            public static class EntryPoint
            {
                public static bool Execute(
                    IDictionary<string, object> arguments,
                    Action<string> logInformation)
                {
                    if (arguments == null)
                    {
                        throw new ArgumentNullException(nameof(arguments));
                    }

                    if (logInformation == null)
                    {
                        throw new ArgumentNullException(nameof(logInformation));
                    }

                    logInformation("Hello from {{namespaceName}}.");
                    return true;
                }
            }
        }

        """;

    private static string CreateObsPluginSource() => """
        #include <obs-module.h>

        #define FOUNDRY_FILTER_ID "dev.creatorsforge.passthrough-filter"

        static const char *foundry_filter_name(void *type_data)
        {
            UNUSED_PARAMETER(type_data);
            return "Foundry Passthrough Filter";
        }

        struct foundry_filter_context {
            obs_source_t *source;
        };

        static void *foundry_filter_create(obs_data_t *settings, obs_source_t *source)
        {
            UNUSED_PARAMETER(settings);
            struct foundry_filter_context *context = bzalloc(sizeof(*context));
            context->source = source;
            return context;
        }

        static void foundry_filter_destroy(void *data)
        {
            bfree(data);
        }

        static void foundry_filter_render(void *data, gs_effect_t *effect)
        {
            UNUSED_PARAMETER(effect);
            struct foundry_filter_context *context = data;
            obs_source_skip_video_filter(context->source);
        }

        static struct obs_source_info foundry_filter = {
            .id = FOUNDRY_FILTER_ID,
            .type = OBS_SOURCE_TYPE_FILTER,
            .output_flags = OBS_SOURCE_VIDEO,
            .get_name = foundry_filter_name,
            .create = foundry_filter_create,
            .destroy = foundry_filter_destroy,
            .video_render = foundry_filter_render,
        };

        bool foundry_obs_plugin_load(void)
        {
            obs_register_source(&foundry_filter);
            return true;
        }

        """;

    private static string CreateObsModuleName(string projectId)
    {
        var builder = new StringBuilder(projectId.Length);
        var previousWasSeparator = false;
        foreach (var character in projectId.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string CreateStreamerBotDefinitionJson(
        FoundryProjectManifest manifest,
        string templateId = FoundryProjectTemplateService.StreamerBotCommand,
        string author = "",
        string? description = null)
    {
        var command = "!" + manifest.Id.Split('.').Last();
        var usesCommand = string.Equals(
            templateId,
            FoundryProjectTemplateService.StreamerBotCommand,
            StringComparison.Ordinal);
        var definition = new
        {
            schemaVersion = 6,
            metadata = new
            {
                author,
                description = description ?? $"{manifest.Name} package generated by Creators Forge Foundry.",
                minimumVersion = "1.0.0-alpha.1",
            },
            queues = new[]
            {
                new
                {
                    id = "default",
                    name = manifest.Name,
                    blocking = false,
                },
            },
            commands = usesCommand ? new[]
            {
                new
                {
                    id = "default",
                    name = manifest.Name,
                    commands = new[] { command },
                    enabled = true,
                    caseSensitive = false,
                    globalCooldown = 0,
                    userCooldown = 0,
                },
            } : [],
            actions = new[]
            {
                new
                {
                    id = "default",
                    name = manifest.Name,
                    enabled = true,
                    queueId = "default",
                    concurrent = false,
                    alwaysRun = false,
                    triggers = new[]
                    {
                        new
                        {
                            id = "command",
                            kind = usesCommand ? "command" : "test",
                            enabled = true,
                            commandId = usesCommand ? "default" : null,
                        },
                    },
            subActions = string.Equals(templateId, FoundryProjectTemplateService.StreamerBotExtension,
                    StringComparison.Ordinal)
                ? Array.Empty<object>()
                : new object[]
                    {
                        new
                        {
                            id = "bridge",
                            kind = "executeBridge",
                            enabled = true,
                            variableName = (string?)null,
                            value = (string?)null,
                            autoType = false,
                        },
                    },
                },
            },
            resources = Array.Empty<object>(),
        };
        return JsonSerializer.Serialize(definition, ManifestSerializerOptions) + "\n";
    }

    private static string CreateStreamerBotTestDefinitionJson(string namespaceName)
    {
        var definition = new
        {
            schemaVersion = 1,
            provider = "streamerbot",
            profiles = FoundryStreamerBotProfiles.Ordered.ToArray(),
            cases = new[]
            {
                new
                {
                    id = "execute-entry-point",
                    name = "Executes the generated entry point",
                    @event = new
                    {
                        kind = "test",
                        name = "Foundry generated project test",
                        arguments = new { },
                    },
                    assertions = new object[]
                    {
                        new { kind = "returnEquals", expected = true },
                        new { kind = "logContains", expected = $"Hello from {namespaceName}." },
                        new { kind = "cphCallCount", key = "CPH.LogInfo", expected = 1 },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(definition, ManifestSerializerOptions) + "\n";
    }

    private static string CreateObsTestDefinitionJson(
        FoundryProjectManifest manifest,
        string templateId)
    {
        var testsSourceLifecycle = templateId is
            FoundryProjectTemplateService.ObsPassthroughFilter or
            FoundryProjectTemplateService.ObsConfigurableFilter or
            FoundryProjectTemplateService.ObsVideoInput;
        var componentId = manifest.ObsPlugin!.Design!.ComponentId;
        var assertions = new List<object>
        {
            new { kind = "abiExport", key = "obs_module_ver", expected = true },
            new { kind = "abiExport", key = "obs_module_set_pointer", expected = true },
            new { kind = "abiExport", key = "obs_module_load", expected = true },
            new { kind = "moduleLoadSucceeded", key = (string?)null, expected = true },
        };
        if (testsSourceLifecycle)
        {
            assertions.Add(new { kind = "sourceRegistered", key = componentId, expected = true });
            assertions.Add(new { kind = "sourceCreated", key = (string?)null, expected = true });
            assertions.Add(new { kind = "sourceDestroyed", key = (string?)null, expected = true });
        }

        var definition = new
        {
            schemaVersion = 1,
            provider = "obsstudio",
            profiles = new[] { "32.x-windows-x64" },
            cases = new[]
            {
                new
                {
                    id = testsSourceLifecycle ? "module-source-lifecycle" : "module-load",
                    name = testsSourceLifecycle
                        ? "Loads the module and completes the source lifecycle"
                        : "Loads the generated OBS module",
                    @event = new
                    {
                        kind = testsSourceLifecycle ? "obs-source-lifecycle" : "obs-module-load",
                        name = testsSourceLifecycle ? componentId : null,
                        arguments = new { },
                    },
                    assertions,
                },
            },
        };
        return JsonSerializer.Serialize(definition, ManifestSerializerOptions) + "\n";
    }

    private static void TryDeleteNewProjectDirectory(string projectDirectory)
    {
        try
        {
            if (Directory.Exists(projectDirectory))
            {
                Directory.Delete(projectDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the original creation failure. Any partial files remain
            // visible in the newly requested project directory.
        }
    }
}
