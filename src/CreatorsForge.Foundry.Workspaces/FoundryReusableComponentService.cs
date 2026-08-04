using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryReusableComponentDescriptor(
    string Id,
    string Version,
    string Name,
    string Provider,
    string Language,
    string Description,
    IReadOnlyDictionary<string, string> Files);

public static class FoundryReusableComponentService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static IReadOnlyList<FoundryReusableComponentDescriptor> Components { get; } =
    [
        new(
            "creatorsforge.managed.arguments",
            "1.0.0",
            "Typed argument reader",
            "streamerbot",
            "C# 7.3",
            "Safely reads required and optional values from Streamer.bot argument dictionaries.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/Components/FoundryArguments.cs"] = ManagedArguments,
            }),
        new(
            "creatorsforge.managed.cooldown",
            "1.0.0",
            "In-memory cooldown gate",
            "streamerbot",
            "C# 7.3",
            "A thread-safe, reusable cooldown gate with explicit UTC time input for testing.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/Components/FoundryCooldown.cs"] = ManagedCooldown,
            }),
        new(
            "creatorsforge.native.owned-context",
            "1.0.0",
            "Owned source context",
            "obsstudio",
            "C17",
            "A small bzalloc/bfree context pair that makes OBS source ownership explicit.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/components/foundry_owned_context.h"] = NativeOwnedContextHeader,
                ["src/components/foundry_owned_context.c"] = NativeOwnedContextSource,
            }),
        new(
            "creatorsforge.native.settings",
            "1.0.0",
            "OBS settings helpers",
            "obsstudio",
            "C17",
            "Defensive helpers for bounded numeric and non-empty string OBS settings.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/components/foundry_settings.h"] = NativeSettingsHeader,
                ["src/components/foundry_settings.c"] = NativeSettingsSource,
            }),
    ];

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> InstallAsync(
        FoundryWorkspace workspace,
        string componentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var component = Components.FirstOrDefault(item =>
            string.Equals(item.Id, componentId, StringComparison.Ordinal));
        if (component is null)
        {
            return Failure("CFW1201", $"Reusable component '{componentId}' was not found.", workspace.ProjectPath);
        }

        if (!string.Equals(component.Provider, workspace.Manifest.Target?.Provider, StringComparison.Ordinal))
        {
            return Failure("CFW1202", $"{component.Name} is not compatible with this project provider.", workspace.ProjectPath);
        }

        if ((workspace.Manifest.Components ?? []).Any(item => string.Equals(item.Id, component.Id, StringComparison.Ordinal)))
        {
            return Failure("CFW1203", $"{component.Name} is already installed in this project.", workspace.ProjectPath);
        }

        foreach (var relativePath in component.Files.Keys)
        {
            var path = ResolveProjectPath(workspace.ProjectRoot, relativePath);
            if (path is null || File.Exists(path))
            {
                return Failure("CFW1204", $"Component installation would replace an existing or unsafe file: {relativePath}", workspace.ProjectPath);
            }
        }

        var compiledSources = component.Files.Keys
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var reference = new FoundryComponentReference
        {
            Id = component.Id,
            Version = component.Version,
            Sources = component.Files.Keys.Order(StringComparer.Ordinal).ToArray(),
        };
        var manifest = workspace.Manifest with
        {
            Components = (workspace.Manifest.Components ?? []).Append(reference).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            ManagedBuild = workspace.Manifest.ManagedBuild is null ? null : workspace.Manifest.ManagedBuild with
            {
                Sources = workspace.Manifest.ManagedBuild.Sources.Concat(compiledSources.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
            },
            NativeBuild = workspace.Manifest.NativeBuild is null ? null : workspace.Manifest.NativeBuild with
            {
                Sources = workspace.Manifest.NativeBuild.Sources.Concat(compiledSources.Where(path => path.EndsWith(".c", StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
            },
        };
        var diagnostics = FoundryProjectValidator.Validate(manifest, workspace.ProjectPath);
        if (diagnostics.Any(item => item.IsError))
        {
            return new(null, diagnostics);
        }

        var written = new List<string>();
        try
        {
            foreach (var file in component.Files.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var path = ResolveProjectPath(workspace.ProjectRoot, file.Key)!;
                await AtomicFile.WriteTextAsync(path, file.Value, cancellationToken).ConfigureAwait(false);
                written.Add(path);
            }

            var json = JsonSerializer.Serialize(manifest, SerializerOptions);
            await AtomicFile.WriteTextAsync(workspace.ProjectPath, json + "\n", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DeleteWrittenFiles(written);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteWrittenFiles(written);
            return Failure("CFW1205", $"The component could not be installed: {exception.Message}", workspace.ProjectPath);
        }

        return await FoundryWorkspaceService.OpenAsync(workspace.ProjectPath, cancellationToken).ConfigureAwait(false);
    }

    private static string? ResolveProjectPath(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static void DeleteWrittenFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try { File.Delete(path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static WorkspaceOperationResult<FoundryWorkspace> Failure(string code, string message, string path) =>
        new(null, [new FoundryDiagnostic(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(path))]);

    private const string ManagedArguments = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;

        namespace CreatorsForge.Components
        {
            public static class FoundryArguments
            {
                public static bool TryGet<T>(IDictionary<string, object> arguments, string name, out T value)
                {
                    value = default(T);
                    object raw;
                    if (arguments == null || !arguments.TryGetValue(name, out raw) || raw == null) return false;
                    if (raw is T typed) { value = typed; return true; }
                    try { value = (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture); return true; }
                    catch (Exception exception) when (exception is InvalidCastException || exception is FormatException || exception is OverflowException) { return false; }
                }
            }
        }
        """;

    private const string ManagedCooldown = """
        using System;
        using System.Collections.Concurrent;

        namespace CreatorsForge.Components
        {
            public sealed class FoundryCooldown
            {
                private readonly ConcurrentDictionary<string, DateTimeOffset> expires = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);

                public bool TryEnter(string key, TimeSpan duration, DateTimeOffset utcNow)
                {
                    if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A cooldown key is required.", nameof(key));
                    DateTimeOffset current;
                    if (expires.TryGetValue(key, out current) && current > utcNow) return false;
                    expires[key] = utcNow.Add(duration);
                    return true;
                }
            }
        }
        """;

    private const string NativeOwnedContextHeader = """
        #pragma once
        #include <obs-module.h>
        struct foundry_owned_context { obs_source_t *source; };
        struct foundry_owned_context *foundry_owned_context_create(obs_source_t *source);
        void foundry_owned_context_destroy(struct foundry_owned_context *context);
        """;

    private const string NativeOwnedContextSource = """
        #include "foundry_owned_context.h"
        struct foundry_owned_context *foundry_owned_context_create(obs_source_t *source)
        {
            struct foundry_owned_context *context = bzalloc(sizeof(*context));
            if (context != NULL) context->source = source;
            return context;
        }
        void foundry_owned_context_destroy(struct foundry_owned_context *context) { bfree(context); }
        """;

    private const string NativeSettingsHeader = """
        #pragma once
        #include <obs-module.h>
        double foundry_setting_clamp_double(obs_data_t *settings, const char *name, double minimum, double maximum);
        const char *foundry_setting_nonempty_string(obs_data_t *settings, const char *name, const char *fallback);
        """;

    private const string NativeSettingsSource = """
        #include "foundry_settings.h"
        double foundry_setting_clamp_double(obs_data_t *settings, const char *name, double minimum, double maximum)
        {
            double value = obs_data_get_double(settings, name);
            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }
        const char *foundry_setting_nonempty_string(obs_data_t *settings, const char *name, const char *fallback)
        {
            const char *value = obs_data_get_string(settings, name);
            return value != NULL && value[0] != '\0' ? value : fallback;
        }
        """;
}
