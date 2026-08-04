using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Testing;

public sealed record FoundryTestExplorerEntry(
    string Context,
    string Id,
    string Name,
    FoundryTestOutcome Outcome,
    FoundryTestCaseResult TestCase,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public string OutcomeText => Outcome.ToString();

    public bool Matches(string? text, FoundryTestOutcome? outcome)
    {
        if (outcome is not null && Outcome != outcome)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(text) ||
            Context.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            Diagnostics.Any(item =>
                item.Code.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                item.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    [SuppressMessage(
        "Globalization",
        "CA1305:Specify IFormatProvider",
        Justification = "Test result details intentionally follow the desktop user's locale.")]
    public string CreateDetails()
    {
        var builder = new StringBuilder()
            .AppendLine($"{Name} [{Outcome.ToString().ToUpperInvariant()}]")
            .AppendLine($"Context: {Context}")
            .AppendLine($"ID: {Id}")
            .AppendLine($"Event: {TestCase.Event.Kind} {TestCase.Event.Name}".TrimEnd())
            .AppendLine($"Duration: {TestCase.DurationMilliseconds} ms")
            .AppendLine($"Return value: {TestCase.ReturnValue?.ToString() ?? "n/a"}")
            .AppendLine()
            .AppendLine("Arguments:")
            .AppendLine(JsonSerializer.Serialize(TestCase.Event.Arguments, DisplayOptions))
            .AppendLine()
            .AppendLine("Assertions:");
        foreach (var assertion in TestCase.Assertions)
        {
            builder.AppendLine(
                $"- {assertion.Outcome}: {assertion.Kind}" +
                (assertion.Key is null ? string.Empty : $" ({assertion.Key})") +
                $" - expected {assertion.Expected.GetRawText()}, actual {assertion.Actual.GetRawText()}");
        }

        builder.AppendLine().AppendLine("Logs:");
        foreach (var log in TestCase.Logs)
        {
            builder.AppendLine(log);
        }

        builder.AppendLine().AppendLine("CPH calls:");
        foreach (var call in TestCase.CphCalls)
        {
            builder.AppendLine($"{call.Method}({string.Join(", ", call.Arguments.Select(item => item.GetRawText()))})");
        }

        return builder.ToString().TrimEnd();
    }

    private static readonly JsonSerializerOptions DisplayOptions = new() { WriteIndented = true };
}

public static class FoundryTestExplorerProjection
{
    public static IReadOnlyList<FoundryTestExplorerEntry> FromRun(FoundryTestRunResult result) =>
        result.Cases.Select(item => new FoundryTestExplorerEntry(
            $"{result.Provider} / {result.Profile}",
            item.Id,
            item.Name,
            item.Outcome,
            item,
            item.Diagnostics)).ToArray();

    public static IReadOnlyList<FoundryTestExplorerEntry> FromMatrix(FoundryCompatibilityMatrixResult matrix) =>
        matrix.Cells.SelectMany(cell => cell.Result.Cases.Select(item => new FoundryTestExplorerEntry(
            $"{cell.Provider} / {cell.Profile} / {cell.RuntimeVersion}",
            item.Id,
            item.Name,
            item.Outcome,
            item,
            item.Diagnostics.Concat(cell.Result.Diagnostics).Distinct().ToArray()))).ToArray();
}
