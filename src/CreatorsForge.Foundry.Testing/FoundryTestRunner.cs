using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Testing;

public sealed class FoundryTestRunner
{
    public const string RelativeResultPath = "test-results/latest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<FoundryTestRunResult> RunAsync(
        FoundryProjectManifest manifest,
        string projectPath,
        string managedAssemblyPath,
        string? resultRelativePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedAssemblyPath);
        var started = DateTimeOffset.UtcNow;
        var projectRoot = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var resultPath = ResolveResultPath(projectRoot, resultRelativePath);
        var diagnostics = new List<FoundryDiagnostic>();
        var cases = new List<FoundryTestCaseResult>();

        if (!string.Equals(manifest.Target?.Provider, FoundryTestProviders.StreamerBot, StringComparison.Ordinal) ||
            !manifest.Features.MockRuntime || manifest.CphInlineBridge is null)
        {
            diagnostics.Add(Error(
                "CFT2001",
                "Phase 11A can run Streamer.bot projects with mockRuntime and args-log-v1.",
                projectPath,
                "$.testDefinition"));
        }
        else if (string.IsNullOrWhiteSpace(manifest.TestDefinition))
        {
            diagnostics.Add(Error("CFT2002", "The project does not declare testDefinition.", projectPath, "$.testDefinition"));
        }
        else
        {
            var definitionPath = ResolveProjectPath(projectRoot, manifest.TestDefinition);
            var loaded = await FoundryTestDefinitionLoader.LoadAsync(
                definitionPath,
                manifest.Target!.Provider,
                cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(loaded.Diagnostics);
            if (loaded.IsSuccess)
            {
                foreach (var testCase in loaded.Definition!.Cases)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    cases.Add(RunCase(managedAssemblyPath, manifest.CphInlineBridge, testCase));
                }
            }
        }

        var outcome = diagnostics.Any(item => item.IsError) || cases.Any(item => item.Outcome == FoundryTestOutcome.Error)
            ? FoundryTestOutcome.Error
            : cases.Any(item => item.Outcome == FoundryTestOutcome.Failed)
                ? FoundryTestOutcome.Failed
                : cases.Count > 0
                    ? FoundryTestOutcome.Passed
                    : FoundryTestOutcome.Skipped;
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
        await WriteResultAsync(resultPath, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static FoundryTestCaseResult RunCase(
        string managedAssemblyPath,
        FoundryCphInlineBridge bridge,
        FoundryTestCaseDefinition testCase)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var invocation = StreamerBotMockInvoker.Invoke(
                managedAssemblyPath,
                bridge,
                testCase.Event);
            var assertions = testCase.Assertions
                .Select(item => Evaluate(item, invocation))
                .ToArray();
            stopwatch.Stop();
            return new(
                testCase.Id,
                testCase.Name,
                testCase.Event,
                assertions.All(item => item.Outcome == FoundryTestOutcome.Passed)
                    ? FoundryTestOutcome.Passed
                    : FoundryTestOutcome.Failed,
                stopwatch.ElapsedMilliseconds,
                invocation.ReturnValue,
                invocation.Logs,
                invocation.Calls,
                assertions,
                []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or BadImageFormatException)
        {
            stopwatch.Stop();
            return new(
                testCase.Id,
                testCase.Name,
                testCase.Event,
                FoundryTestOutcome.Error,
                stopwatch.ElapsedMilliseconds,
                null,
                [],
                [],
                [],
                [Error("CFT2003", exception.Message, managedAssemblyPath, "$")]);
        }
    }

    private static FoundryTestAssertionResult Evaluate(
        FoundryTestAssertionDefinition assertion,
        StreamerBotMockInvocationResult invocation)
    {
        JsonElement actual;
        var passed = assertion.Kind switch
        {
            FoundryTestAssertionKinds.ReturnEquals => Compare(
                assertion.Expected,
                actual = JsonSerializer.SerializeToElement(invocation.ReturnValue)),
            FoundryTestAssertionKinds.LogContains => CompareLogContains(
                assertion.Expected,
                invocation.Logs,
                out actual),
            FoundryTestAssertionKinds.LogEquals => Compare(
                assertion.Expected,
                actual = JsonSerializer.SerializeToElement(invocation.Logs)),
            FoundryTestAssertionKinds.ArgumentEquals => CompareArgument(
                assertion,
                invocation.Arguments,
                out actual),
            FoundryTestAssertionKinds.CphCallCount => CompareCallCount(
                assertion,
                invocation.Calls,
                out actual),
            _ => SetUnsupported(out actual),
        };
        return new(
            assertion.Kind,
            assertion.Key,
            passed ? FoundryTestOutcome.Passed : FoundryTestOutcome.Failed,
            assertion.Expected.Clone(),
            actual,
            passed ? "Assertion passed." : "Expected and actual values differ.");
    }

    private static bool CompareArgument(
        FoundryTestAssertionDefinition assertion,
        IReadOnlyDictionary<string, object?> arguments,
        out JsonElement actual)
    {
        if (!arguments.TryGetValue(assertion.Key!, out var value))
        {
            actual = JsonSerializer.SerializeToElement<object?>(null);
            return false;
        }

        actual = JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));
        return Compare(assertion.Expected, actual);
    }

    private static bool CompareLogContains(
        JsonElement expected,
        IReadOnlyList<string> logs,
        out JsonElement actual)
    {
        actual = JsonSerializer.SerializeToElement(logs);
        return expected.ValueKind == JsonValueKind.String &&
               logs.Any(log => log.Contains(expected.GetString()!, StringComparison.Ordinal));
    }

    private static bool CompareCallCount(
        FoundryTestAssertionDefinition assertion,
        IReadOnlyList<FoundryMockCphCall> calls,
        out JsonElement actual)
    {
        var count = calls.Count(item => string.Equals(item.Method, assertion.Key, StringComparison.Ordinal));
        actual = JsonSerializer.SerializeToElement(count);
        return Compare(assertion.Expected, actual);
    }

    private static bool Compare(JsonElement expected, JsonElement actual) =>
        string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal);

    private static bool SetUnsupported(out JsonElement actual)
    {
        actual = JsonSerializer.SerializeToElement<object?>(null);
        return false;
    }

    private static string ResolveProjectPath(string projectRoot, string relativePath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The test definition escaped the project directory.");
        }

        return path;
    }

    internal static string ResolveResultPath(string projectRoot, string? relativePath)
    {
        var buildRoot = Path.Combine(Path.GetFullPath(projectRoot), "build");
        var value = string.IsNullOrWhiteSpace(relativePath) ? RelativeResultPath : relativePath;
        var path = Path.GetFullPath(Path.Combine(buildRoot, value.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(buildRoot) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The test result path escaped the project build directory.");
        }

        return path;
    }

    internal static async Task WriteResultAsync(
        string path,
        FoundryTestRunResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(result, JsonOptions) + "\n",
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static FoundryDiagnostic Error(string code, string message, string path, string jsonPath) => new(
        code,
        FoundryDiagnosticSeverity.Error,
        message,
        new(path, jsonPath));
}
