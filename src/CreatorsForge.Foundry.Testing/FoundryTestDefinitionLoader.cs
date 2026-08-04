using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Testing;

public static partial class FoundryTestDefinitionLoader
{
    private static readonly HashSet<string> StreamerBotAssertions = new(
        [
            FoundryTestAssertionKinds.ReturnEquals,
            FoundryTestAssertionKinds.LogContains,
            FoundryTestAssertionKinds.LogEquals,
            FoundryTestAssertionKinds.ArgumentEquals,
            FoundryTestAssertionKinds.CphCallCount,
        ],
        StringComparer.Ordinal);
    private static readonly HashSet<string> ObsAssertions = new(
        [
            FoundryTestAssertionKinds.AbiExport,
            FoundryTestAssertionKinds.ModuleLoadSucceeded,
            FoundryTestAssertionKinds.SourceRegistered,
            FoundryTestAssertionKinds.SourceCreated,
            FoundryTestAssertionKinds.SourceDestroyed,
        ],
        StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<FoundryTestDefinitionLoadResult> LoadAsync(
        string path,
        string expectedProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProvider);
        var diagnostics = new List<FoundryDiagnostic>();
        if (!File.Exists(path))
        {
            diagnostics.Add(Error("CFT1001", "The test definition does not exist.", path, "$"));
            return new(null, diagnostics);
        }

        FoundryTestDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<FoundryTestDefinition>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add(Error("CFT1002", $"The test definition could not be loaded: {exception.Message}", path, "$"));
            return new(null, diagnostics);
        }

        if (definition is null)
        {
            diagnostics.Add(Error("CFT1002", "The test definition is empty.", path, "$"));
            return new(null, diagnostics);
        }

        Validate(definition, expectedProvider, path, diagnostics);
        return new(definition, diagnostics);
    }

    private static void Validate(
        FoundryTestDefinition definition,
        string expectedProvider,
        string path,
        List<FoundryDiagnostic> diagnostics)
    {
        if (definition.SchemaVersion != FoundryTestDefinition.CurrentSchemaVersion)
        {
            diagnostics.Add(Error("CFT1003", $"Test schema version {definition.SchemaVersion} is not supported.", path, "$.schemaVersion"));
        }

        if (!string.Equals(definition.Provider, expectedProvider, StringComparison.Ordinal))
        {
            diagnostics.Add(Error("CFT1004", "The test provider does not match the project target.", path, "$.provider"));
        }

        var supportedProfiles = string.Equals(expectedProvider, FoundryTestProviders.StreamerBot, StringComparison.Ordinal)
            ? FoundryTestProfiles.StreamerBot
            : FoundryTestProfiles.ObsStudio;
        var profiles = definition.Profiles ?? [];
        var seenProfiles = new HashSet<string>(StringComparer.Ordinal);
        for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
        {
            var profile = profiles[profileIndex];
            if (!supportedProfiles.Contains(profile) || !seenProfiles.Add(profile))
            {
                diagnostics.Add(Error(
                    "CFT1014",
                    $"Compatibility profile '{profile}' is unsupported or duplicated for '{expectedProvider}'.",
                    path,
                    $"$.profiles[{profileIndex}]"));
            }
        }

        if (definition.Cases is null || definition.Cases.Count == 0)
        {
            diagnostics.Add(Error("CFT1005", "At least one test case is required.", path, "$.cases"));
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var caseIndex = 0; caseIndex < definition.Cases.Count; caseIndex++)
        {
            var testCase = definition.Cases[caseIndex];
            var casePath = $"$.cases[{caseIndex}]";
            if (!TestIdPattern().IsMatch(testCase.Id) || !ids.Add(testCase.Id))
            {
                diagnostics.Add(Error("CFT1006", "Test case IDs must be unique lowercase identifiers.", path, $"{casePath}.id"));
            }

            if (string.IsNullOrWhiteSpace(testCase.Name) ||
                testCase.Event is null ||
                string.IsNullOrWhiteSpace(testCase.Event.Kind) ||
                testCase.Event.Arguments is null)
            {
                diagnostics.Add(Error("CFT1007", "Each test requires a name and simulated event kind.", path, casePath));
            }
            else if (string.Equals(expectedProvider, FoundryTestProviders.ObsStudio, StringComparison.Ordinal) &&
                     testCase.Event.Kind is not ("obs-module-load" or "obs-source-lifecycle"))
            {
                diagnostics.Add(Error("CFT1013", "OBS tests require obs-module-load or obs-source-lifecycle events.", path, $"{casePath}.event.kind"));
            }
            else if (string.Equals(testCase.Event.Kind, "obs-source-lifecycle", StringComparison.Ordinal) &&
                     string.IsNullOrWhiteSpace(testCase.Event.Name))
            {
                diagnostics.Add(Error("CFT1013", "An OBS source lifecycle event requires the source ID in event.name.", path, $"{casePath}.event.name"));
            }

            if (testCase.Assertions is null || testCase.Assertions.Count == 0)
            {
                diagnostics.Add(Error("CFT1008", "Each test requires at least one assertion.", path, $"{casePath}.assertions"));
                continue;
            }

            for (var assertionIndex = 0; assertionIndex < testCase.Assertions.Count; assertionIndex++)
            {
                var assertion = testCase.Assertions[assertionIndex];
                if (!FoundryTestAssertionKinds.Supported.Contains(assertion.Kind))
                {
                    diagnostics.Add(Error("CFT1009", $"Assertion kind '{assertion.Kind}' is not supported.", path, $"{casePath}.assertions[{assertionIndex}].kind"));
                }
                else if (string.Equals(expectedProvider, FoundryTestProviders.StreamerBot, StringComparison.Ordinal)
                             ? !StreamerBotAssertions.Contains(assertion.Kind)
                             : !ObsAssertions.Contains(assertion.Kind))
                {
                    diagnostics.Add(Error("CFT1012", $"Assertion '{assertion.Kind}' is not valid for provider '{expectedProvider}'.", path, $"{casePath}.assertions[{assertionIndex}].kind"));
                }
                else if ((assertion.Kind is FoundryTestAssertionKinds.ArgumentEquals or
                        FoundryTestAssertionKinds.CphCallCount or
                        FoundryTestAssertionKinds.AbiExport or
                        FoundryTestAssertionKinds.SourceRegistered) &&
                         string.IsNullOrWhiteSpace(assertion.Key))
                {
                    diagnostics.Add(Error("CFT1010", $"Assertion '{assertion.Kind}' requires key.", path, $"{casePath}.assertions[{assertionIndex}].key"));
                }

                if (assertion.Expected.ValueKind == JsonValueKind.Undefined)
                {
                    diagnostics.Add(Error("CFT1011", "Every assertion requires an expected value.", path, $"{casePath}.assertions[{assertionIndex}].expected"));
                }
            }
        }
    }

    private static FoundryDiagnostic Error(string code, string message, string path, string jsonPath) => new(
        code,
        FoundryDiagnosticSeverity.Error,
        message,
        new(path, jsonPath));

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TestIdPattern();
}
