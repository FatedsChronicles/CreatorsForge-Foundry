using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build.ObsStudio;

internal sealed class ObsPluginBuildPipeline(IBuildProcessRunner processRunner)
{
    private static readonly System.Text.RegularExpressions.Regex NativeDiagnosticPattern = new(
        @"^(?<file>.+)\((?<line>\d+)(?:,(?<column>\d+))?\):\s+(?<severity>warning|error|fatal error)\s+(?<code>[A-Z]+\d+):\s+(?<message>.*?)(?:\s+\[[^\]]+\])?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly System.Text.RegularExpressions.Regex LinkerDiagnosticPattern = new(
        @"^(?<file>[^:]+):\s+(?<severity>warning|error|fatal error)\s+(?<code>LNK\d+):\s+(?<message>.*)$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<FoundryBuildResult> BuildAsync(
        FoundryProjectManifest manifest,
        string projectPath,
        IReadOnlyList<FoundryDiagnostic> initialDiagnostics,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<FoundryDiagnostic>(initialDiagnostics);
        var projectRoot = Path.GetDirectoryName(projectPath)!;
        var outputRoot = Path.Combine(projectRoot, "build");
        var nativeOutput = Path.Combine(outputRoot, "obs", "bin");
        var packageOutput = Path.Combine(outputRoot, "obs", "package");
        var intermediate = Path.Combine(outputRoot, "obj", "obs");
        var cmakeBuild = Path.Combine(intermediate, "cmake-build");

        var sources = ResolveSources(manifest, projectRoot, projectPath, diagnostics);
        ObsSdkStatus? sdk = null;
        if (manifest.ObsPlugin!.SdkVersion is not null)
        {
            sdk = ObsSdkManager.Inspect();
            if (!sdk.IsReady)
            {
                diagnostics.Add(Error(
                    "CFB1010",
                    $"Pinned OBS SDK {manifest.ObsPlugin.SdkVersion} is not ready: {sdk.Message}",
                    projectPath,
                    "$.obsPlugin.sdkVersion",
                    "Run 'foundry sdk install obsstudio' and rebuild."));
            }
        }
        if (diagnostics.Any(item => item.IsError))
        {
            return new(outputRoot, null, null, diagnostics);
        }

        try
        {
            ResetGeneratedDirectory(projectRoot, nativeOutput);
            ResetGeneratedDirectory(projectRoot, packageOutput);
            ResetGeneratedDirectory(projectRoot, intermediate);
            Directory.CreateDirectory(nativeOutput);
            Directory.CreateDirectory(packageOutput);
            Directory.CreateDirectory(intermediate);

            await File.WriteAllTextAsync(
                Path.Combine(intermediate, "foundry-obs-module.c"),
                GenerateModuleAdapter(manifest.ObsPlugin),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(intermediate, "CMakeLists.txt"),
                GenerateCMake(manifest, sources, nativeOutput, sdk),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error("CFB1001", $"OBS build inputs could not be prepared: {exception.Message}", projectPath, "$.nativeBuild", "Check access to the generated build directory."));
            return new(outputRoot, null, null, diagnostics);
        }

        var configureArguments = new List<string>
        {
            "-S", intermediate,
            "-B", cmakeBuild,
        };
        var generator = Environment.GetEnvironmentVariable("CMAKE_GENERATOR");
        if (string.IsNullOrWhiteSpace(generator) ||
            generator.StartsWith("Visual Studio", StringComparison.OrdinalIgnoreCase))
        {
            configureArguments.AddRange(["-A", "x64"]);
        }
        else
        {
            configureArguments.Add("-DCMAKE_BUILD_TYPE=Release");
        }
        foreach (var variable in new[]
                 {
                     "CMAKE_MAKE_PROGRAM",
                     "CMAKE_RC_COMPILER",
                     "CMAKE_MT",
                 })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                configureArguments.Add($"-D{variable}={value.Replace('\\', '/')}");
            }
        }
        if (sdk is not null)
        {
            configureArguments.Add($"-Dlibobs_DIR={Path.Combine(sdk.SdkRoot, "cmake")}");
        }
        var configure = await RunAsync(
            new("cmake", projectRoot, configureArguments),
            "CFB1002",
            "CMake configuration",
            projectPath,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        if (!configure)
        {
            return new(outputRoot, null, null, diagnostics);
        }

        var compiled = await RunAsync(
            new("cmake", projectRoot,
            [
                "--build", cmakeBuild,
                "--config", "Release",
                "--parallel",
            ]),
            "CFB1003",
            "OBS native compilation",
            projectPath,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        if (!compiled)
        {
            return new(outputRoot, null, null, diagnostics);
        }

        var plugin = manifest.ObsPlugin!;
        var binaryPath = Path.Combine(nativeOutput, $"{plugin.ModuleName}.dll");
        if (!File.Exists(binaryPath))
        {
            diagnostics.Add(Error("CFB1004", $"Native compilation did not produce {plugin.ModuleName}.dll.", projectPath, "$.obsPlugin.moduleName", "Review the CMake build output."));
            return new(outputRoot, null, null, diagnostics);
        }

        try
        {
            var packagePath = Path.Combine(
                packageOutput,
                $"{plugin.ModuleName}-{manifest.Version}-windows-x64.zip");
            await WritePackageAsync(manifest, binaryPath, packagePath, cancellationToken)
                .ConfigureAwait(false);
            var artifacts = new[]
            {
                await CreateArtifactAsync(FoundryPackageArtifactKinds.NativeObsPlugin, $"obs/bin/{plugin.ModuleName}.dll", binaryPath, cancellationToken).ConfigureAwait(false),
                await CreateArtifactAsync(FoundryPackageArtifactKinds.ObsPluginPackage, $"obs/package/{Path.GetFileName(packagePath)}", packagePath, cancellationToken).ConfigureAwait(false),
            };
            var packageIr = new FoundryPackageIntermediate
            {
                Project = new(manifest.Id, manifest.Name, manifest.Version),
                Target = new(
                    manifest.Target!.Provider,
                    manifest.Target.Profile,
                    "native-c17-windows-x64",
                    ObsApiVersion: plugin.ApiVersion,
                    ObsSdkVersion: plugin.SdkVersion,
                    ObsTemplateRevision: plugin.Design?.Template,
                    ObsComponentId: plugin.Design?.ComponentId),
                Artifacts = artifacts,
            };
            var packageIrPath = Path.Combine(outputRoot, "package-ir.json");
            await File.WriteAllTextAsync(
                packageIrPath,
                JsonSerializer.Serialize(packageIr, JsonOptions) + "\n",
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            return new(outputRoot, packageIrPath, packageIr, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            diagnostics.Add(Error("CFB1005", $"OBS package generation failed: {exception.Message}", projectPath, "$.outputs", "Check the generated output directory and try again."));
            return new(outputRoot, null, null, diagnostics);
        }
    }

    private async Task<bool> RunAsync(
        BuildProcessRequest request,
        string code,
        string operation,
        string projectPath,
        List<FoundryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(ParseNativeDiagnostics(result));
            if (result.ExitCode == 0)
            {
                return true;
            }

            diagnostics.Add(new(
                code,
                FoundryDiagnosticSeverity.Error,
                $"{operation} failed with exit code {result.ExitCode}.",
                new FoundryDiagnosticLocation(projectPath, "$.nativeBuild"),
                "Install CMake and the Visual Studio C++ x64 toolchain, then correct any native compiler diagnostics.",
                string.Join(Environment.NewLine, result.StandardOutput, result.StandardError).Trim()));
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            diagnostics.Add(Error(code, $"{operation} could not start: {exception.Message}", projectPath, "$.nativeBuild", "Install CMake and the Visual Studio C++ x64 build tools."));
            return false;
        }
    }

    private static FoundryDiagnostic[] ParseNativeDiagnostics(
        BuildProcessResult result)
    {
        var lines = string.Join("\n", result.StandardOutput, result.StandardError)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var diagnostics = new List<FoundryDiagnostic>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var match = NativeDiagnosticPattern.Match(line);
            if (match.Success)
            {
                _ = int.TryParse(match.Groups["line"].Value, out var lineNumber);
                int? columnNumber = int.TryParse(
                    match.Groups["column"].Value,
                    out var parsedColumn)
                    ? parsedColumn
                    : null;
                diagnostics.Add(new(
                    match.Groups["code"].Value,
                    ParseNativeSeverity(match.Groups["severity"].Value),
                    match.Groups["message"].Value,
                    new FoundryDiagnosticLocation(
                        match.Groups["file"].Value,
                        Line: lineNumber,
                        Column: columnNumber)));
                continue;
            }

            match = LinkerDiagnosticPattern.Match(line);
            if (match.Success)
            {
                diagnostics.Add(new(
                    match.Groups["code"].Value,
                    ParseNativeSeverity(match.Groups["severity"].Value),
                    match.Groups["message"].Value,
                    new FoundryDiagnosticLocation(match.Groups["file"].Value.Trim())));
            }
        }

        return diagnostics.Distinct().ToArray();
    }

    private static FoundryDiagnosticSeverity ParseNativeSeverity(string severity) =>
        severity.Contains("warning", StringComparison.OrdinalIgnoreCase)
            ? FoundryDiagnosticSeverity.Warning
            : FoundryDiagnosticSeverity.Error;

    private static List<string> ResolveSources(
        FoundryProjectManifest manifest,
        string projectRoot,
        string projectPath,
        List<FoundryDiagnostic> diagnostics)
    {
        var root = Path.TrimEndingDirectorySeparator(projectRoot) + Path.DirectorySeparatorChar;
        var result = new List<string>();
        for (var index = 0; index < manifest.NativeBuild!.Sources.Count; index++)
        {
            var source = manifest.NativeBuild.Sources[index];
            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, source.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                diagnostics.Add(Error("CFB1000", $"Native source '{source}' does not exist beneath the project directory.", projectPath, $"$.nativeBuild.sources[{index}]", "Create the source file or correct the manifest path."));
            }
            else
            {
                result.Add(fullPath);
            }
        }

        return result;
    }

    private static string GenerateModuleAdapter(FoundryObsPlugin plugin)
    {
        if (plugin.SdkVersion is not null)
        {
            return $$"""
                /* Generated by Creators Forge Foundry. Do not edit generated output. */
                #include <obs-module.h>

                OBS_DECLARE_MODULE()
                extern bool {{plugin.EntrySymbol}}(void);

                bool obs_module_load(void) { return {{plugin.EntrySymbol}}(); }
                MODULE_EXPORT const char *obs_module_name(void) { return "{{EscapeCString(plugin.DisplayName)}}"; }
                MODULE_EXPORT const char *obs_module_author(void) { return "{{EscapeCString(plugin.Author)}}"; }
                MODULE_EXPORT const char *obs_module_description(void) { return "{{EscapeCString(plugin.Description)}}"; }
                """ + "\n";
        }

        var api = Version.Parse(plugin.ApiVersion);
        var encoded = ((uint)api.Major << 24) | ((uint)api.Minor << 16) | (uint)api.Build;
        return $$"""
            /* Generated by Creators Forge Foundry. Do not edit generated output. */
            #include <stdbool.h>
            #include <stdint.h>

            #if !defined(_WIN32) || !defined(_M_X64)
            #error "The Phase 8 OBS module contract requires Windows x64."
            #endif

            #define FOUNDRY_EXPORT __declspec(dllexport)
            static void *foundry_obs_module_pointer;
            extern bool {{plugin.EntrySymbol}}(void);

            FOUNDRY_EXPORT uint32_t obs_module_ver(void) { return UINT32_C(0x{{encoded:X8}}); }
            FOUNDRY_EXPORT void obs_module_set_pointer(void *module) { foundry_obs_module_pointer = module; }
            FOUNDRY_EXPORT bool obs_module_load(void) { return {{plugin.EntrySymbol}}(); }
            FOUNDRY_EXPORT const char *obs_module_name(void) { return "{{EscapeCString(plugin.DisplayName)}}"; }
            FOUNDRY_EXPORT const char *obs_module_author(void) { return "{{EscapeCString(plugin.Author)}}"; }
            FOUNDRY_EXPORT const char *obs_module_description(void) { return "{{EscapeCString(plugin.Description)}}"; }
            """ + "\n";
    }

    private static string GenerateCMake(
        FoundryProjectManifest manifest,
        IReadOnlyList<string> sources,
        string nativeOutput,
        ObsSdkStatus? sdk)
    {
        var sourceItems = Enumerable.Repeat("${CMAKE_CURRENT_SOURCE_DIR}/foundry-obs-module.c", 1)
            .Concat(sources.Select(ToCMakePath))
            .Select(path => $"  \"{path}\"");
        var findPackage = sdk is null
            ? string.Empty
            : $"find_package(libobs {manifest.ObsPlugin!.SdkVersion} EXACT REQUIRED CONFIG)";
        var targetLink = sdk is null
            ? string.Empty
            : $"target_link_libraries({manifest.ObsPlugin!.ModuleName} PRIVATE OBS::libobs)";
        return $$"""
            cmake_minimum_required(VERSION 3.20)
            project(FoundryObsModule LANGUAGES C)

            {{findPackage}}
            add_library({{manifest.ObsPlugin!.ModuleName}} MODULE
            {{string.Join("\n", sourceItems)}}
            )
            {{targetLink}}
            set_target_properties({{manifest.ObsPlugin.ModuleName}} PROPERTIES
              C_STANDARD 17
              C_STANDARD_REQUIRED ON
              PREFIX ""
              OUTPUT_NAME "{{manifest.ObsPlugin.ModuleName}}"
              RUNTIME_OUTPUT_DIRECTORY "{{ToCMakePath(nativeOutput)}}"
              LIBRARY_OUTPUT_DIRECTORY "{{ToCMakePath(nativeOutput)}}"
              RUNTIME_OUTPUT_DIRECTORY_RELEASE "{{ToCMakePath(nativeOutput)}}"
              LIBRARY_OUTPUT_DIRECTORY_RELEASE "{{ToCMakePath(nativeOutput)}}"
            )
            if(MSVC)
              target_compile_options({{manifest.ObsPlugin.ModuleName}} PRIVATE /W4 /WX /Brepro)
              target_link_options({{manifest.ObsPlugin.ModuleName}} PRIVATE /Brepro /INCREMENTAL:NO)
            endif()
            """ + "\n";
    }

    private static async Task WritePackageAsync(
        FoundryProjectManifest manifest,
        string binaryPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var binaryEntry = archive.CreateEntry($"obs-plugins/64bit/{manifest.ObsPlugin!.ModuleName}.dll", CompressionLevel.NoCompression);
        binaryEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var entryStream = binaryEntry.Open())
        await using (var source = new FileStream(binaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
        {
            await source.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
        }

        var manifestEntry = archive.CreateEntry("foundry-package.json", CompressionLevel.NoCompression);
        manifestEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, new
        {
            schemaVersion = 1,
            projectId = manifest.Id,
            projectVersion = manifest.Version,
            provider = "obsstudio",
            profile = manifest.Target!.Profile,
            moduleName = manifest.ObsPlugin.ModuleName,
            apiVersion = manifest.ObsPlugin.ApiVersion,
            sdkVersion = manifest.ObsPlugin.SdkVersion,
            templateRevision = manifest.ObsPlugin.Design?.Template,
            componentId = manifest.ObsPlugin.Design?.ComponentId,
            architecture = manifest.NativeBuild!.Architecture,
        }, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FoundryPackageArtifact> CreateArtifactAsync(string kind, string relativePath, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new(kind, relativePath, stream.Length, Convert.ToHexStringLower(hash));
    }

    private static void ResetGeneratedDirectory(string projectRoot, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Generated path escaped the project directory.");
        }

        if (Directory.Exists(fullPath))
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Generated path is a file-system link.");
            }
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private static string EscapeCString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string ToCMakePath(string value) => value.Replace('\\', '/').Replace("\"", "\\\"", StringComparison.Ordinal);

    private static FoundryDiagnostic Error(string code, string message, string projectPath, string jsonPath, string fix) =>
        new(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(projectPath, jsonPath), fix);
}
