using System.Text.Json;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Testing;

namespace CreatorsForge.Foundry.Testing.Tests;

public sealed class FoundryTestExplorerPresentationTests
{
    [Fact]
    public void ProjectionFlattensMatrixCellsAndPreservesRuntimeContext()
    {
        var stable = CreateRun("1.0.4-stable", FoundryTestOutcome.Passed);
        var beta = CreateRun("1.0.5-beta.1", FoundryTestOutcome.Failed);
        var matrix = new FoundryCompatibilityMatrixResult
        {
            ProjectId = stable.ProjectId,
            ProjectVersion = stable.ProjectVersion,
            StartedAtUtc = stable.StartedAtUtc,
            FinishedAtUtc = beta.FinishedAtUtc,
            Outcome = FoundryTestOutcome.Failed,
            Cells =
            [
                new("stable", "streamerbot", "1.0.4-stable", "mock-runtime-v1", null, stable.Outcome, stable),
                new("beta", "streamerbot", "1.0.5-beta.1", "mock-runtime-v1", null, beta.Outcome, beta),
            ],
        };

        var entries = FoundryTestExplorerProjection.FromMatrix(matrix);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, item => item.Context.Contains("1.0.4-stable", StringComparison.Ordinal));
        Assert.Contains(entries, item => item.Context.Contains("1.0.5-beta.1", StringComparison.Ordinal));
        Assert.Contains(entries, item => item.Outcome == FoundryTestOutcome.Failed);
    }

    [Fact]
    public void EntryFiltersByOutcomeIdentityContextAndDiagnostics()
    {
        var testCase = CreateCase(FoundryTestOutcome.Error) with
        {
            Diagnostics =
            [
                new(
                    "CFT2999",
                    FoundryDiagnosticSeverity.Error,
                    "Lifecycle callback failed",
                    new("filter.c", Line: 42, Column: 3)),
            ],
        };
        var entry = new FoundryTestExplorerEntry(
            "obsstudio / 32.x-windows-x64 / 32.1.2",
            testCase.Id,
            testCase.Name,
            testCase.Outcome,
            testCase,
            testCase.Diagnostics);

        Assert.True(entry.Matches("lifecycle", FoundryTestOutcome.Error));
        Assert.True(entry.Matches("32.1.2", null));
        Assert.True(entry.Matches("CFT2999", null));
        Assert.False(entry.Matches("different", null));
        Assert.False(entry.Matches(null, FoundryTestOutcome.Passed));
        Assert.Contains("Assertions:", entry.CreateDetails(), StringComparison.Ordinal);
        Assert.Contains("Logs:", entry.CreateDetails(), StringComparison.Ordinal);
    }

    private static FoundryTestRunResult CreateRun(string profile, FoundryTestOutcome outcome)
    {
        var now = DateTimeOffset.UtcNow;
        return new()
        {
            ProjectId = "com.creatorsforge.tests.explorer",
            ProjectVersion = "1.0.0",
            Provider = "streamerbot",
            Profile = profile,
            StartedAtUtc = now,
            FinishedAtUtc = now,
            Outcome = outcome,
            Cases = [CreateCase(outcome)],
        };
    }

    private static FoundryTestCaseResult CreateCase(FoundryTestOutcome outcome) => new(
        "source-lifecycle",
        "Source lifecycle",
        new()
        {
            Kind = "obs-source-lifecycle",
            Name = "dev.creatorsforge.filter",
            Arguments = new Dictionary<string, JsonElement>(),
        },
        outcome,
        12,
        true,
        ["Lifecycle complete"],
        [],
        [
            new(
                "sourceDestroyed",
                null,
                outcome,
                JsonSerializer.SerializeToElement(true),
                JsonSerializer.SerializeToElement(outcome == FoundryTestOutcome.Passed),
                "Lifecycle assertion"),
        ],
        []);
}
