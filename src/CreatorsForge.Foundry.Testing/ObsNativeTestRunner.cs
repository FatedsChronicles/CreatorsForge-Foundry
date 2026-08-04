using System.Diagnostics;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Testing;

public static class ObsNativeTestRunner
{
    public static async Task<FoundryTestRunResult> RunAsync(
        FoundryProjectManifest manifest,
        string projectPath,
        string pluginPath,
        string obsRoot,
        string nativeHostAssembly,
        TimeSpan? timeout = null,
        string? resultRelativePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var started = DateTimeOffset.UtcNow;
        var projectRoot = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var resultPath = FoundryTestRunner.ResolveResultPath(projectRoot, resultRelativePath);
        var diagnostics = new List<FoundryDiagnostic>();
        var cases = new List<FoundryTestCaseResult>();
        FoundryTestDefinition? definition = null;
        ObsAbiInspection? abi = null;

        if (!string.Equals(manifest.Target?.Provider, FoundryTestProviders.ObsStudio, StringComparison.Ordinal) ||
            manifest.ObsPlugin is null)
        {
            diagnostics.Add(Error("CFT2100", "The native lifecycle runner requires an OBS plugin project.", projectPath));
        }
        else if (string.IsNullOrWhiteSpace(manifest.TestDefinition))
        {
            diagnostics.Add(Error("CFT2002", "The project does not declare testDefinition.", projectPath));
        }
        else
        {
            var definitionPath = ResolveProjectPath(projectRoot, manifest.TestDefinition);
            var loaded = await FoundryTestDefinitionLoader.LoadAsync(
                definitionPath,
                FoundryTestProviders.ObsStudio,
                cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(loaded.Diagnostics);
            definition = loaded.IsSuccess ? loaded.Definition : null;
        }

        if (definition is not null)
        {
            var executable = Path.Combine(Path.GetFullPath(obsRoot), "bin", "64bit", "obs64.exe");
            if (!File.Exists(executable))
            {
                diagnostics.Add(Error("CFT2106", "The selected OBS root does not contain bin/64bit/obs64.exe.", obsRoot));
            }
            else
            {
                var versionText = FileVersionInfo.GetVersionInfo(executable).FileVersion ?? string.Empty;
                if (!FoundryObsCompatibility.IsSupportedRuntime(versionText))
                {
                    diagnostics.Add(Error(
                        "CFT2106",
                        $"OBS {versionText} is not an exact supported runtime ({FoundryObsCompatibility.SupportedRuntimeDisplay}).",
                        executable));
                }
            }

            try
            {
                abi = ObsAbiInspector.Inspect(pluginPath);
                if (!abi.IsPortableExecutable || !abi.IsX64 || !abi.IsDll)
                {
                    diagnostics.Add(Error("CFT2101", "The native artifact is not a Windows x64 DLL.", pluginPath));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException)
            {
                diagnostics.Add(Error("CFT2101", $"OBS ABI inspection failed: {exception.Message}", pluginPath));
            }
        }

        if (definition is not null && abi is not null && diagnostics.All(item => !item.IsError))
        {
            var expectedSourceIds = definition.Cases
                .Where(item => string.Equals(item.Event.Kind, "obs-source-lifecycle", StringComparison.Ordinal))
                .Select(item => item.Event.Name)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (expectedSourceIds.Length > 1)
            {
                diagnostics.Add(Error(
                    "CFT2105",
                    "One isolated OBS run currently supports one lifecycle source ID.",
                    Path.Combine(projectRoot, manifest.TestDefinition!)));
            }

            if (diagnostics.All(item => !item.IsError))
            {
                var runDirectory = Path.Combine(
                    projectRoot,
                    "build",
                    "test-results",
                    "native",
                    Guid.NewGuid().ToString("N"));
                var process = await ObsNativeProcessRunner.RunAsync(
                    new ObsNativeHostRequest
                    {
                        PluginPath = Path.GetFullPath(pluginPath),
                        ObsRoot = Path.GetFullPath(obsRoot),
                        ExpectedSourceId = expectedSourceIds.SingleOrDefault(),
                    },
                    nativeHostAssembly,
                    runDirectory,
                    timeout ?? TimeSpan.FromSeconds(20),
                    cancellationToken).ConfigureAwait(false);
                if (!process.Completed)
                {
                    diagnostics.Add(new(
                        process.TimedOut ? "CFT2103" : "CFT2102",
                        FoundryDiagnosticSeverity.Error,
                        process.Failure ?? "The native test host did not complete.",
                        new(pluginPath),
                        "Inspect the isolated host output and native callback lifecycle.",
                        string.Join(Environment.NewLine, [process.StandardOutput, process.StandardError]).Trim()));
                }
                else
                {
                    foreach (var testCase in definition.Cases)
                    {
                        cases.Add(CreateCase(testCase, abi, process.HostResult!));
                    }
                }
            }
        }

        var outcome = diagnostics.Any(item => item.IsError)
            ? FoundryTestOutcome.Error
            : cases.Any(item => item.Outcome == FoundryTestOutcome.Failed)
                ? FoundryTestOutcome.Failed
                : cases.Count > 0 ? FoundryTestOutcome.Passed : FoundryTestOutcome.Skipped;
        var result = new FoundryTestRunResult
        {
            ProjectId = manifest.Id,
            ProjectVersion = manifest.Version,
            Provider = manifest.Target?.Provider ?? string.Empty,
            Profile = manifest.Target?.Profile ?? string.Empty,
            StartedAtUtc = started,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Outcome = outcome,
            Cases = cases,
            Diagnostics = diagnostics,
            ResultPath = resultPath,
        };
        await FoundryTestRunner.WriteResultAsync(resultPath, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static FoundryTestCaseResult CreateCase(
        FoundryTestCaseDefinition definition,
        ObsAbiInspection abi,
        ObsNativeHostResult host)
    {
        var stopwatch = Stopwatch.StartNew();
        var assertions = definition.Assertions.Select(assertion =>
        {
            var actualValue = assertion.Kind switch
            {
                FoundryTestAssertionKinds.AbiExport => abi.Exports.Contains(assertion.Key!, StringComparer.Ordinal),
                FoundryTestAssertionKinds.ModuleLoadSucceeded => host.ModuleLoadSucceeded,
                FoundryTestAssertionKinds.SourceRegistered =>
                    host.RegisteredSourceIds.Contains(assertion.Key!, StringComparer.Ordinal),
                FoundryTestAssertionKinds.SourceCreated => host.SourceCreated,
                FoundryTestAssertionKinds.SourceDestroyed => host.SourceDestroyed,
                _ => false,
            };
            var actual = JsonSerializer.SerializeToElement(actualValue);
            var passed = assertion.Expected.ValueKind == JsonValueKind.True && actualValue ||
                         assertion.Expected.ValueKind == JsonValueKind.False && !actualValue;
            return new FoundryTestAssertionResult(
                assertion.Kind,
                assertion.Key,
                passed ? FoundryTestOutcome.Passed : FoundryTestOutcome.Failed,
                assertion.Expected.Clone(),
                actual,
                passed ? "Assertion passed." : "Expected and actual values differ.");
        }).ToArray();
        stopwatch.Stop();
        return new(
            definition.Id,
            definition.Name,
            definition.Event,
            assertions.All(item => item.Outcome == FoundryTestOutcome.Passed)
                ? FoundryTestOutcome.Passed
                : FoundryTestOutcome.Failed,
            stopwatch.ElapsedMilliseconds,
            host.ModuleLoadSucceeded,
            [],
            [],
            assertions,
            []);
    }

    private static string ResolveProjectPath(string root, string relative)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The test definition escaped the project directory.");
        }

        return path;
    }

    private static FoundryDiagnostic Error(string code, string message, string path) => new(
        code,
        FoundryDiagnosticSeverity.Error,
        message,
        new(path));
}
