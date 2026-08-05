using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CreatorsForge.Foundry.Build.ObsStudio;

public sealed record ObsSdkStatus(
    bool IsReady,
    string Version,
    string SdkRoot,
    string? Message);

public sealed class ObsSdkManager
{
    public const string Version = "32.1.2";
    public const string CacheEnvironmentVariable = "CREATORS_FORGE_FOUNDRY_SDK_CACHE";
    public const string SourceArchiveName = "OBS-Studio-32.1.2-Sources.tar.gz";
    public const string WindowsArchiveName = "OBS-Studio-32.1.2-Windows-x64.zip";
    public const string SourceArchiveSha256 = "c6532380c68a75327fe8b551461adeca8f184dcbe4015096251a6de76362a554";
    public const string WindowsArchiveSha256 = "8d97e4563bd8d22d03e63042aa7dccede1d555c9bd35ce8a9e5019b0d0201bf6";

    private const string SourceArchiveUrl =
        "https://github.com/obsproject/obs-studio/releases/download/32.1.2/OBS-Studio-32.1.2-Sources.tar.gz";
    private const string WindowsArchiveUrl =
        "https://github.com/obsproject/obs-studio/releases/download/32.1.2/OBS-Studio-32.1.2-Windows-x64.zip";

    private static readonly Regex DumpbinExportPattern = new(
        @"^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(?<name>\S+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static string GetDefaultCacheRoot()
    {
        var configured = Environment.GetEnvironmentVariable(CacheEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Creators Forge Foundry",
                "sdk")
            : Path.GetFullPath(configured);
    }

    public static string GetSdkRoot(string? cacheRoot = null) => Path.Combine(
        Path.GetFullPath(cacheRoot ?? GetDefaultCacheRoot()),
        "obsstudio",
        Version);

    public static ObsSdkStatus Inspect(string? cacheRoot = null)
    {
        var sdkRoot = GetSdkRoot(cacheRoot);
        var required = new[]
        {
            Path.Combine(sdkRoot, "sdk-manifest.json"),
            Path.Combine(sdkRoot, "sources", "libobs", "obs-module.h"),
            Path.Combine(sdkRoot, "sources", "libobs", "obsconfig.h"),
            Path.Combine(sdkRoot, "bin", "x64", "obs.dll"),
            Path.Combine(sdkRoot, "lib", "x64", "obs.lib"),
            Path.Combine(sdkRoot, "cmake", "libobsConfig.cmake"),
            Path.Combine(sdkRoot, "cmake", "libobsConfigVersion.cmake"),
        };
        var missing = required.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            return new(false, Version, sdkRoot, $"Missing {Path.GetRelativePath(sdkRoot, missing)}.");
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<InstalledSdkManifest>(
                File.ReadAllText(required[0]),
                ReadJsonOptions);
            if (manifest is null ||
                !string.Equals(manifest.Version, Version, StringComparison.Ordinal) ||
                !string.Equals(
                    HashFile(required[3]),
                    manifest.ObsDllSha256,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    HashFile(required[4]),
                    manifest.ObsImportLibrarySha256,
                    StringComparison.Ordinal))
            {
                return new(false, Version, sdkRoot, "The SDK manifest or installed file hashes are invalid.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, Version, sdkRoot, $"The SDK manifest could not be verified: {exception.Message}");
        }

        return new(true, Version, sdkRoot, null);
    }

    public static async Task<ObsSdkStatus> InstallAsync(
        string? cacheRoot = null,
        string? archiveDirectory = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        await InstallWithToolchainAsync(
            cacheRoot,
            archiveDirectory,
            progress,
            null,
            cancellationToken).ConfigureAwait(false);

    public static async Task<ObsSdkStatus> InstallWithToolchainAsync(
        string? cacheRoot = null,
        string? archiveDirectory = null,
        IProgress<string>? progress = null,
        string? visualStudioInstallationRoot = null,
        CancellationToken cancellationToken = default)
    {
        var existing = Inspect(cacheRoot);
        if (existing.IsReady)
        {
            return existing;
        }

        var resolvedCache = Path.GetFullPath(cacheRoot ?? GetDefaultCacheRoot());
        var downloads = archiveDirectory is null
            ? Path.Combine(resolvedCache, "downloads")
            : Path.GetFullPath(archiveDirectory);
        Directory.CreateDirectory(downloads);
        var sourceArchive = Path.Combine(downloads, SourceArchiveName);
        var windowsArchive = Path.Combine(downloads, WindowsArchiveName);
        await EnsureArchiveAsync(
            sourceArchive,
            SourceArchiveUrl,
            SourceArchiveSha256,
            progress,
            cancellationToken).ConfigureAwait(false);
        await EnsureArchiveAsync(
            windowsArchive,
            WindowsArchiveUrl,
            WindowsArchiveSha256,
            progress,
            cancellationToken).ConfigureAwait(false);

        var sdkRoot = GetSdkRoot(resolvedCache);
        var parent = Path.GetDirectoryName(sdkRoot)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            progress?.Report("Extracting official OBS source headers...");
            var sourceStaging = Path.Combine(staging, "source-archive");
            Directory.CreateDirectory(sourceStaging);
            await using (var source = new FileStream(sourceArchive, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gzip, sourceStaging, overwriteFiles: false);
            }

            var extractedRoot = Directory.GetDirectories(sourceStaging).Single();
            var libobsSource = Path.Combine(extractedRoot, "libobs");
            if (!File.Exists(Path.Combine(libobsSource, "obs-module.h")))
            {
                throw new InvalidDataException("The official source archive does not contain libobs/obs-module.h.");
            }

            var installedSources = Path.Combine(staging, "sources", "libobs");
            CopyDirectory(libobsSource, installedSources);
            await File.WriteAllTextAsync(
                Path.Combine(installedSources, "obsconfig.h"),
                GenerateObsConfigHeader(),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);

            progress?.Report("Extracting the matching OBS runtime export surface...");
            var obsDll = Path.Combine(staging, "bin", "x64", "obs.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(obsDll)!);
            using (var archive = ZipFile.OpenRead(windowsArchive))
            {
                var entry = archive.GetEntry("bin/64bit/obs.dll") ??
                    throw new InvalidDataException("The official Windows archive does not contain bin/64bit/obs.dll.");
                await using var input = entry.Open();
                await using var output = new FileStream(obsDll, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("Generating the MSVC x64 libobs import library...");
            var libraryDirectory = Path.Combine(staging, "lib", "x64");
            Directory.CreateDirectory(libraryDirectory);
            var definitionPath = Path.Combine(libraryDirectory, "obs.def");
            var importLibraryPath = Path.Combine(libraryDirectory, "obs.lib");
            await GenerateImportLibraryAsync(
                obsDll,
                definitionPath,
                importLibraryPath,
                visualStudioInstallationRoot,
                cancellationToken).ConfigureAwait(false);

            var cmakeDirectory = Path.Combine(staging, "cmake");
            Directory.CreateDirectory(cmakeDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(cmakeDirectory, "libobsConfig.cmake"),
                GenerateCMakeConfig(),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(cmakeDirectory, "libobsConfigVersion.cmake"),
                GenerateCMakeVersionConfig(),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);

            var manifest = new InstalledSdkManifest(
                1,
                Version,
                SourceArchiveUrl,
                SourceArchiveSha256,
                WindowsArchiveUrl,
                WindowsArchiveSha256,
                HashFile(obsDll),
                HashFile(importLibraryPath));
            await File.WriteAllTextAsync(
                Path.Combine(staging, "sdk-manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptions) + "\n",
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(sdkRoot))
            {
                Directory.Delete(sdkRoot, recursive: true);
            }
            Directory.Move(staging, sdkRoot);
            progress?.Report($"OBS SDK {Version} is ready.");
            return Inspect(resolvedCache);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static async Task EnsureArchiveAsync(
        string path,
        string url,
        string expectedHash,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path) && string.Equals(HashFile(path), expectedHash, StringComparison.Ordinal))
        {
            progress?.Report($"Using verified {Path.GetFileName(path)}.");
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
        progress?.Report($"Downloading {Path.GetFileName(path)}...");
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        await using (var input = await HttpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        var actualHash = HashFile(path);
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            File.Delete(path);
            throw new InvalidDataException(
                $"Checksum mismatch for {Path.GetFileName(path)}. Expected {expectedHash}, received {actualHash}.");
        }
    }

    private static async Task GenerateImportLibraryAsync(
        string obsDll,
        string definitionPath,
        string importLibraryPath,
        string? visualStudioInstallationRoot,
        CancellationToken cancellationToken)
    {
        var toolchain = VisualStudioToolchainService.Resolve(visualStudioInstallationRoot);
        if (toolchain?.IsReady != true)
        {
            throw new FileNotFoundException(
                toolchain?.Summary ?? "Visual Studio C++ x64 build tools were not found.");
        }
        var dumpbin = toolchain.DumpbinPath!;
        var librarian = toolchain.LibrarianPath!;
        var exports = await RunProcessAsync(
            dumpbin,
            ["/nologo", "/exports", obsDll],
            cancellationToken).ConfigureAwait(false);
        if (exports.ExitCode != 0)
        {
            throw new InvalidOperationException($"dumpbin failed: {exports.StandardError}");
        }

        var names = exports.StandardOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => DumpbinExportPattern.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (names.Length == 0)
        {
            throw new InvalidDataException("No exports were discovered in the pinned obs.dll.");
        }

        await File.WriteAllTextAsync(
            definitionPath,
            "LIBRARY obs.dll\nEXPORTS\n" + string.Join("\n", names.Select(name => $"    {name}")) + "\n",
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        var library = await RunProcessAsync(
            librarian,
            ["/nologo", $"/def:{definitionPath}", "/machine:x64", $"/out:{importLibraryPath}"],
            cancellationToken).ConfigureAwait(false);
        if (library.ExitCode != 0 || !File.Exists(importLibraryPath))
        {
            throw new InvalidOperationException(
                $"The MSVC librarian failed: {library.StandardOutput} {library.StandardError}".Trim());
        }
    }

    private static async Task<ProcessOutput> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} did not start.");
        }
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new(process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
    }

    private static string GenerateObsConfigHeader() => """
        #pragma once
        #define OBS_DATA_PATH "data/libobs/"
        #define OBS_PLUGIN_PATH "obs-plugins/64bit"
        #define OBS_PLUGIN_DESTINATION "obs-plugins/64bit"
        #define OBS_INSTALL_PREFIX ""
        #define OBS_RELEASE_CANDIDATE 0
        #define OBS_BETA 0

        """;

    private static string GenerateCMakeConfig() => """
        get_filename_component(_FOUNDRY_OBS_SDK_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
        if(NOT TARGET OBS::libobs)
          add_library(OBS::libobs SHARED IMPORTED)
          set_target_properties(OBS::libobs PROPERTIES
            IMPORTED_IMPLIB "${_FOUNDRY_OBS_SDK_ROOT}/lib/x64/obs.lib"
            IMPORTED_LOCATION "${_FOUNDRY_OBS_SDK_ROOT}/bin/x64/obs.dll"
            INTERFACE_INCLUDE_DIRECTORIES "${_FOUNDRY_OBS_SDK_ROOT}/sources/libobs"
          )
        endif()
        set(libobs_VERSION "32.1.2")

        """;

    private static string GenerateCMakeVersionConfig() => """
        set(PACKAGE_VERSION "32.1.2")
        if(PACKAGE_FIND_VERSION VERSION_EQUAL PACKAGE_VERSION)
          set(PACKAGE_VERSION_EXACT TRUE)
          set(PACKAGE_VERSION_COMPATIBLE TRUE)
        else()
          set(PACKAGE_VERSION_COMPATIBLE FALSE)
        endif()

        """;

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CreatorsForge-Foundry/1.0");
        return client;
    }

    private sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);

    private sealed record InstalledSdkManifest(
        int SchemaVersion,
        string Version,
        string SourceArchiveUrl,
        string SourceArchiveSha256,
        string WindowsArchiveUrl,
        string WindowsArchiveSha256,
        string ObsDllSha256,
        string ObsImportLibrarySha256);
}
