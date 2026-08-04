using System.Globalization;
using System.Reflection.PortableExecutable;
using CreatorsForge.Foundry.Core.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace CreatorsForge.Foundry.Editor;

public interface IRoslynEditorService
{
    Task<EditorAnalysisResult> AnalyzeAsync(
        IReadOnlyList<EditorSourceDocument> sources,
        CancellationToken cancellationToken = default);

    Task<string> FormatAsync(
        IReadOnlyList<EditorSourceDocument> sources,
        string documentPath,
        CancellationToken cancellationToken = default);

    Task<EditorSourceLocation?> FindDefinitionAsync(
        IReadOnlyList<EditorSourceDocument> sources,
        string documentPath,
        int position,
        CancellationToken cancellationToken = default);
}

public sealed class RoslynEditorService : IRoslynEditorService
{
    private static readonly Lazy<PortableExecutableReference[]> Net481References =
        new(LoadNet481References);

    public async Task<EditorAnalysisResult> AnalyzeAsync(
        IReadOnlyList<EditorSourceDocument> sources,
        CancellationToken cancellationToken = default)
    {
        using var context = CreateContext(sources);
        var compilation = await context.Project.GetCompilationAsync(
            cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            return new([]);
        }

        var diagnostics = compilation
            .GetDiagnostics(cancellationToken)
            .Where(diagnostic =>
                !diagnostic.IsSuppressed &&
                diagnostic.Severity != DiagnosticSeverity.Hidden)
            .Select(ToFoundryDiagnostic)
            .OrderBy(diagnostic => diagnostic.Location?.FilePath, PathComparer)
            .ThenBy(diagnostic => diagnostic.Location?.Line)
            .ThenBy(diagnostic => diagnostic.Location?.Column)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();
        return new(diagnostics);
    }

    public async Task<string> FormatAsync(
        IReadOnlyList<EditorSourceDocument> sources,
        string documentPath,
        CancellationToken cancellationToken = default)
    {
        using var context = CreateContext(sources);
        var document = FindDocument(context.Project, documentPath) ??
            throw new ArgumentException(
                "The document is not part of the editor project.",
                nameof(documentPath));
        var formatted = await Formatter.FormatAsync(
            document,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = await formatted.GetTextAsync(cancellationToken).ConfigureAwait(false);
        return text.ToString();
    }

    public async Task<EditorSourceLocation?> FindDefinitionAsync(
        IReadOnlyList<EditorSourceDocument> sources,
        string documentPath,
        int position,
        CancellationToken cancellationToken = default)
    {
        using var context = CreateContext(sources);
        var document = FindDocument(context.Project, documentPath);
        if (document is null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null || root.FullSpan.IsEmpty)
        {
            return null;
        }

        var boundedPosition = Math.Clamp(position, 0, root.FullSpan.End - 1);
        var token = root.FindToken(boundedPosition);
        var node = token.Parent;
        if (node is null)
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(node, cancellationToken).Symbol;
        for (var current = node; symbol is null && current is not null; current = current.Parent)
        {
            symbol = model.GetSymbolInfo(current, cancellationToken).Symbol ??
                model.GetDeclaredSymbol(current, cancellationToken);
        }

        var location = symbol?.Locations.FirstOrDefault(item =>
            item.IsInSource && item.SourceTree?.FilePath is not null);
        if (location?.SourceTree?.FilePath is null)
        {
            return null;
        }

        var line = location.GetLineSpan().StartLinePosition;
        return new(location.SourceTree.FilePath, line.Line + 1, line.Character + 1);
    }

    private static EditorContext CreateContext(
        IReadOnlyList<EditorSourceDocument> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId("FoundryEditor");
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Foundry Editor",
            "Foundry.Editor.Analysis",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Disable),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp7_3),
            metadataReferences: Net481References.Value);
        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        foreach (var source in sources)
        {
            var documentId = DocumentId.CreateNewId(projectId, source.FilePath);
            solution = solution.AddDocument(
                documentId,
                Path.GetFileName(source.FilePath),
                SourceText.From(source.Text),
                filePath: Path.GetFullPath(source.FilePath));
        }

        if (!workspace.TryApplyChanges(solution))
        {
            workspace.Dispose();
            throw new InvalidOperationException(
                "Roslyn could not initialize the Foundry editor workspace.");
        }

        return new(workspace, workspace.CurrentSolution.GetProject(projectId)!);
    }

    private static Document? FindDocument(Project project, string documentPath)
    {
        var fullPath = Path.GetFullPath(documentPath);
        return project.Documents.FirstOrDefault(document =>
            string.Equals(document.FilePath, fullPath, PathComparison));
    }

    private static PortableExecutableReference[] LoadNet481References()
    {
        var referenceRoot = Path.Combine(
            AppContext.BaseDirectory,
            "ReferenceAssemblies",
            "net481");
        if (!Directory.Exists(referenceRoot))
        {
            throw new DirectoryNotFoundException(
                $"The .NET Framework 4.8.1 editor references were not found at '{referenceRoot}'.");
        }

        return Directory
            .EnumerateFiles(referenceRoot, "*.dll", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Where(HasManagedMetadata)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static bool HasManagedMetadata(string path)
    {
        using var stream = File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var reader = new PEReader(stream);
        return reader.HasMetadata &&
            System.Reflection.Metadata.PEReaderExtensions
                .GetMetadataReader(reader)
                .IsAssembly;
    }

    private static FoundryDiagnostic ToFoundryDiagnostic(Diagnostic diagnostic)
    {
        var severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => FoundryDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => FoundryDiagnosticSeverity.Warning,
            _ => FoundryDiagnosticSeverity.Info,
        };
        FoundryDiagnosticLocation? location = null;
        if (diagnostic.Location is { IsInSource: true, SourceTree.FilePath: { } filePath })
        {
            var start = diagnostic.Location.GetLineSpan().StartLinePosition;
            location = new(filePath, Line: start.Line + 1, Column: start.Character + 1);
        }

        return new(
            diagnostic.Id,
            severity,
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            location,
            Details: "Roslyn editor diagnostic");
    }

    private static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed class EditorContext : IDisposable
    {
        public EditorContext(AdhocWorkspace workspace, Project project)
        {
            Workspace = workspace;
            Project = project;
        }

        public AdhocWorkspace Workspace { get; }

        public Project Project { get; }

        public void Dispose() => Workspace.Dispose();
    }
}
