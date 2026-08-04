namespace CreatorsForge.Foundry.Core.Diagnostics;

/// <summary>
/// A stable, machine-readable problem reported by Foundry.
/// </summary>
public sealed record FoundryDiagnostic(
    string Code,
    FoundryDiagnosticSeverity Severity,
    string Message,
    FoundryDiagnosticLocation? Location = null,
    string? SuggestedFix = null,
    string? Details = null)
{
    public bool IsError => Severity == FoundryDiagnosticSeverity.Error;
}

public enum FoundryDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Identifies a file and, where known, a position within it.
/// Lines and columns are one-based.
/// </summary>
public sealed record FoundryDiagnosticLocation(
    string FilePath,
    string? JsonPath = null,
    long? Line = null,
    long? Column = null);
