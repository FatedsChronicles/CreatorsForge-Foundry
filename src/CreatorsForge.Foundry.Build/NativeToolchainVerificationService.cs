using System.Diagnostics;
using System.Text;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Build;

public sealed record NativeToolchainVerificationStage(
    string Id,
    string Name,
    bool Passed,
    TimeSpan Duration,
    string Details,
    string? Command = null);

public sealed record NativeToolchainVerificationResult(
    bool CoreVerificationPassed,
    IReadOnlyList<NativeToolchainVerificationStage> Stages,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess => CoreVerificationPassed &&
        Stages.All(item => item.Passed) &&
        Diagnostics.All(item => !item.IsError);

    public string Summary => IsSuccess
        ? "Native OBS configure and compile verification passed."
        : Diagnostics.FirstOrDefault(item => item.IsError)?.Message ??
          "Native OBS configure and compile verification failed.";
}

/// <summary>
/// Compiles the smallest possible OBS module in an isolated temporary directory.
/// It never writes project sources or changes the process or Windows environment.
/// </summary>
public sealed class NativeToolchainVerificationService
{
    private const string ProbeName = "creators-forge-toolchain-probe";
    private readonly IBuildProcessRunner processRunner;
    private readonly Func<string?, string?, NativeToolchainReadiness> inspectReadiness;
    private readonly string temporaryRoot;

    public NativeToolchainVerificationService(
        IBuildProcessRunner? processRunner = null,
        Func<string?, string?, NativeToolchainReadiness>? inspectReadiness = null,
        string? temporaryRoot = null)
    {
        this.processRunner = processRunner ?? new DotNetBuildProcessRunner();
        this.inspectReadiness = inspectReadiness ?? ((visualStudio, cmake) =>
            NativeToolchainReadinessService.Inspect(visualStudio, cmake));
        this.temporaryRoot = Path.GetFullPath(temporaryRoot ?? Path.Combine(
            Path.GetTempPath(),
            "CreatorsForge.Foundry",
            "NativeToolchainVerification"));
    }

    public async Task<NativeToolchainVerificationResult> VerifyAsync(
        string? visualStudioInstallationRoot,
        string? cmakeExecutablePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stages = new List<NativeToolchainVerificationStage>();
        var diagnostics = new List<FoundryDiagnostic>();
        var readinessWatch = Stopwatch.StartNew();
        var readiness = inspectReadiness(visualStudioInstallationRoot, cmakeExecutablePath);
        readinessWatch.Stop();
        stages.Add(new(
            "readiness",
            "Toolchain readiness",
            readiness.IsReady,
            readinessWatch.Elapsed,
            readiness.IsReady
                ? "CMake, MSVC x64, Windows SDK, and the pinned OBS SDK are ready."
                : string.Join(Environment.NewLine, readiness.Checks
                    .Where(item => !item.IsReady)
                    .Select(item => $"{item.Name}: {item.Details}"))));
        if (!readiness.IsReady)
        {
            diagnostics.Add(Error(
                "CFB1101",
                "Native toolchain verification cannot start because one or more readiness checks failed.",
                "Use the recommended selections or reselect the affected tool, then refresh the checks."));
            return new(false, stages, diagnostics);
        }

        var runRoot = Path.Combine(temporaryRoot, $"run-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(runRoot, "source");
        var buildRoot = Path.Combine(runRoot, "build");
        try
        {
            progress?.Report("Preparing a disposable native verification workspace...");
            var prepareWatch = Stopwatch.StartNew();
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "probe.c"),
                ProbeSource,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "CMakeLists.txt"),
                ProbeCMake,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            prepareWatch.Stop();
            stages.Add(new("prepare", "Disposable workspace", true, prepareWatch.Elapsed,
                "Created isolated source and build directories under the system temporary folder."));

            var cmake = readiness.CMake.ExecutablePath!;
            var configureArguments = new List<string>
            {
                "-S", sourceRoot,
                "-B", buildRoot,
                "-A", "x64",
                $"-DCMAKE_GENERATOR_INSTANCE={readiness.VisualStudio!.InstallationRoot.Replace('\\', '/')}",
                $"-Dlibobs_DIR={Path.Combine(readiness.ObsSdk.SdkRoot, "cmake").Replace('\\', '/')}",
            };
            progress?.Report("Configuring the disposable OBS module with CMake...");
            if (!await RunStageAsync(
                    "configure", "CMake configure", "CFB1102", cmake, runRoot,
                    configureArguments, stages, diagnostics, cancellationToken).ConfigureAwait(false))
            {
                return new(false, stages, diagnostics);
            }

            progress?.Report("Compiling and linking the disposable OBS module...");
            if (!await RunStageAsync(
                    "compile", "Native compile and link", "CFB1103", cmake, runRoot,
                    ["--build", buildRoot, "--config", "Release", "--parallel"],
                    stages, diagnostics, cancellationToken).ConfigureAwait(false))
            {
                return new(false, stages, diagnostics);
            }

            var artifactWatch = Stopwatch.StartNew();
            var binaryPath = FindProbeBinary(buildRoot);
            var artifactExists = binaryPath is not null;
            artifactWatch.Stop();
            stages.Add(new(
                "artifact", "Probe artifact", artifactExists, artifactWatch.Elapsed,
                artifactExists
                    ? $"The expected x64 OBS module DLL was produced at {Path.GetRelativePath(buildRoot, binaryPath!)}."
                    : $"No {ProbeName}.dll was found beneath the disposable CMake build directory."));
            if (!artifactExists)
            {
                diagnostics.Add(Error(
                    "CFB1104",
                    "Native compilation completed without producing the verification DLL.",
                    "Review the compile output and confirm the selected generator targets x64."));
                return new(false, stages, diagnostics);
            }

            progress?.Report("Native OBS configure and compile verification passed.");
            return new(true, stages, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostics.Add(Error(
                "CFB1105",
                $"Disposable native verification could not complete: {exception.Message}",
                "Check access to the system temporary directory and try again."));
            return new(false, stages, diagnostics);
        }
        finally
        {
            var cleanupWatch = Stopwatch.StartNew();
            try
            {
                DeleteOwnedWorkspace(runRoot);
                cleanupWatch.Stop();
                stages.Add(new(
                    "cleanup", "Disposable workspace cleanup", true, cleanupWatch.Elapsed,
                    "Removed the isolated verification workspace."));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                cleanupWatch.Stop();
                stages.Add(new(
                    "cleanup", "Disposable workspace cleanup", false, cleanupWatch.Elapsed,
                    exception.Message));
                diagnostics.Add(new(
                    "CFB1106",
                    FoundryDiagnosticSeverity.Warning,
                    $"The disposable verification workspace could not be removed: {exception.Message}",
                    SuggestedFix: "Close processes using the temporary files and retry verification."));
            }
        }
    }

    private async Task<bool> RunStageAsync(
        string id,
        string name,
        string diagnosticCode,
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        List<NativeToolchainVerificationStage> stages,
        List<FoundryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var command = FormatCommand(executable, arguments);
        var watch = Stopwatch.StartNew();
        BuildProcessResult result;
        try
        {
            result = await processRunner.RunAsync(
                new(executable, workingDirectory, arguments),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            watch.Stop();
            stages.Add(new(id, name, false, watch.Elapsed, exception.Message, command));
            diagnostics.Add(Error(diagnosticCode, $"{name} could not start: {exception.Message}",
                "Reselect the tool, refresh readiness checks, and retry verification."));
            return false;
        }
        watch.Stop();

        var output = CombineOutput(result.StandardOutput, result.StandardError);
        var passed = result.ExitCode == 0;
        stages.Add(new(
            id,
            name,
            passed,
            watch.Elapsed,
            passed ? "Completed successfully." : $"Exited with code {result.ExitCode}.",
            command));
        if (!passed)
        {
            diagnostics.Add(new(
                diagnosticCode,
                FoundryDiagnosticSeverity.Error,
                $"{name} failed with exit code {result.ExitCode}.",
                SuggestedFix: "Review the captured tool output, repair or reselect the affected tool, and retry.",
                Details: output));
        }
        return passed;
    }

    private void DeleteOwnedWorkspace(string runRoot)
    {
        var fullRunRoot = Path.GetFullPath(runRoot);
        var ownedPrefix = temporaryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullRunRoot.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullRunRoot).StartsWith("run-", StringComparison.Ordinal))
        {
            return;
        }
        if (Directory.Exists(fullRunRoot))
        {
            Directory.Delete(fullRunRoot, recursive: true);
        }
    }

    private static string? FindProbeBinary(string buildRoot)
    {
        if (!Directory.Exists(buildRoot))
        {
            return null;
        }

        var fullBuildRoot = Path.GetFullPath(buildRoot);
        var ownedPrefix = fullBuildRoot.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(
                fullBuildRoot,
                $"{ProbeName}.dll",
                SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => path.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static FoundryDiagnostic Error(string code, string message, string fix) =>
        new(code, FoundryDiagnosticSeverity.Error, message, SuggestedFix: fix);

    private static string CombineOutput(string output, string error) =>
        string.Join(Environment.NewLine, new[] { output, error }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        .Trim();

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments) =>
        string.Join(" ", new[] { Quote(executable) }.Concat(arguments.Select(Quote)));

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private const string ProbeSource = """
        #include <obs-module.h>

        MODULE_EXPORT uint32_t obs_module_ver(void)
        {
            return LIBOBS_API_VER;
        }

        MODULE_EXPORT bool obs_module_load(void)
        {
            return true;
        }
        """;

    private const string ProbeCMake = """
        cmake_minimum_required(VERSION 3.20)
        project(creators_forge_toolchain_probe LANGUAGES C)
        find_package(libobs CONFIG REQUIRED)
        add_library(creators_forge_toolchain_probe MODULE probe.c)
        target_link_libraries(creators_forge_toolchain_probe PRIVATE OBS::libobs)
        set_target_properties(creators_forge_toolchain_probe PROPERTIES
          PREFIX ""
          OUTPUT_NAME "creators-forge-toolchain-probe"
          RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/verified")
        """;
}
