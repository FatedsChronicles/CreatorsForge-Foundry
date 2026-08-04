using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Editor;

public sealed record EditorSourceDocument(string FilePath, string Text);

public sealed record EditorAnalysisResult(
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public sealed record EditorSourceLocation(
    string FilePath,
    int Line,
    int Column);
