using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record PreviewDesignElement(
    string Kind,
    string Name,
    string Label,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record PreviewDesignSurface(
    string Kind,
    string Source,
    int ViewportWidth,
    int ViewportHeight,
    long SourceLength,
    string SourceSha256,
    IReadOnlyList<PreviewDesignElement> Elements,
    string Notice);

public static class PreviewDesignService
{
    private const long MaximumSourceBytes = 1024 * 1024;
    private const int MaximumElements = 48;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly Regex HtmlElementPattern = new(
        @"<(?<tag>header|nav|main|section|article|aside|footer|h[1-6]|p|button|input|img|div)\b[^>]*>(?<text>[^<]{0,160})?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex WinFormsControlPattern = new(
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?:System\.Windows\.Forms\.)?(?<type>Button|Label|TextBox|RichTextBox|Panel|GroupBox|PictureBox|CheckBox|ComboBox|ListBox|DataGridView|ProgressBar)\s*\(",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<string> GetCandidateSources(
        FoundryWorkspace workspace,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        IEnumerable<string> candidates = kind switch
        {
            FoundryPreview.WinFormsKind => workspace.Manifest.ManagedBuild?.Sources ?? [],
            FoundryPreview.ObsComponentKind => workspace.Manifest.ObsPlugin?.Design is { } design
                ? [design.Source]
                : [],
            FoundryPreview.StaticWebKind => EnumerateFiles(workspace.ProjectTree)
                .Where(path => string.Equals(Path.GetExtension(path), ".html", StringComparison.OrdinalIgnoreCase)),
            _ => [],
        };
        var expectedExtension = kind switch
        {
            FoundryPreview.WinFormsKind => ".cs",
            FoundryPreview.ObsComponentKind => ".c",
            FoundryPreview.StaticWebKind => ".html",
            _ => string.Empty,
        };
        return candidates
            .Where(path => string.Equals(Path.GetExtension(path), expectedExtension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<WorkspaceOperationResult<PreviewDesignSurface>> AnalyzeAsync(
        FoundryWorkspace workspace,
        FoundryPreview preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(preview);

        var diagnostics = FoundryProjectValidator.Validate(
            workspace.Manifest with { Preview = preview },
            workspace.ProjectPath).ToList();
        if (!preview.Enabled)
        {
            diagnostics.Add(Error(
                "CFW2301",
                "Design preview is disabled for this project.",
                workspace.ProjectPath,
                "Enable preview before refreshing the design surface."));
        }
        if (diagnostics.Any(item => item.IsError))
        {
            return new(null, diagnostics);
        }

        var sourcePath = ResolveOwnedPath(workspace.ProjectRoot, preview.Source);
        if (sourcePath is null)
        {
            return Failure("CFW2302", "Preview source must remain inside the project.", workspace.ProjectPath,
                "Choose a source offered by the preview designer.");
        }
        if (!File.Exists(sourcePath))
        {
            return Failure("CFW2303", $"Preview source does not exist: {preview.Source}", sourcePath,
                "Create the source or choose another project file.");
        }

        try
        {
            await using var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            if (stream.Length > MaximumSourceBytes)
            {
                return Failure("CFW2304", "Preview source exceeds the 1 MiB design-time limit.", sourcePath,
                    "Choose a smaller source file.");
            }
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var source = StrictUtf8.GetString(bytes);
            var elements = preview.Kind switch
            {
                FoundryPreview.StaticWebKind => AnalyzeHtml(source, preview),
                FoundryPreview.WinFormsKind => AnalyzeWinForms(source, preview),
                FoundryPreview.ObsComponentKind => AnalyzeObsComponent(workspace, preview),
                _ => [],
            };
            var hash = Convert.ToHexString(SHA256.HashData(bytes))
                .ToLowerInvariant();
            return new(
                new(
                    preview.Kind,
                    preview.Source,
                    preview.Width,
                    preview.Height,
                    bytes.LongLength,
                    hash,
                    elements,
                    "Static structural preview only. Foundry did not execute project code or scripts."),
                diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return Failure("CFW2305", $"Preview source could not be inspected: {exception.Message}", sourcePath,
                "Check file access and UTF-8 encoding, then refresh the preview.");
        }
    }

    private static List<PreviewDesignElement> AnalyzeHtml(
        string source,
        FoundryPreview preview)
    {
        var elements = new List<PreviewDesignElement>();
        var y = 28d;
        foreach (Match match in HtmlElementPattern.Matches(source).Cast<Match>().Take(MaximumElements))
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
            var height = tag switch
            {
                "header" or "nav" or "footer" => 64,
                "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => 52,
                "button" or "input" => 42,
                "img" => 120,
                _ => 76,
            };
            if (y + height > preview.Height - 20) break;
            elements.Add(new(
                tag,
                tag,
                string.IsNullOrWhiteSpace(text) ? $"<{tag}>" : TrimLabel(text),
                28,
                y,
                Math.Max(180, preview.Width - 56),
                height));
            y += height + 12;
        }
        return elements.Count == 0
            ? [new("document", "html", "HTML document (no supported visible elements detected)", 28, 28, Math.Max(180, preview.Width - 56), 80)]
            : elements;
    }

    private static List<PreviewDesignElement> AnalyzeWinForms(
        string source,
        FoundryPreview preview)
    {
        var elements = new List<PreviewDesignElement>();
        var index = 0;
        foreach (Match match in WinFormsControlPattern.Matches(source).Cast<Match>().Take(MaximumElements))
        {
            var name = match.Groups["name"].Value;
            var type = match.Groups["type"].Value;
            var location = ReadPair(source, name, "Location") ??
                (30 + index % 2 * Math.Max(220, (preview.Width - 90) / 2), 40 + index / 2 * 72);
            var size = ReadPair(source, name, "Size") ?? (Math.Max(180, (preview.Width - 90) / 2), 46);
            var label = ReadText(source, name) ?? name;
            elements.Add(new(type.ToLowerInvariant(), name, $"{type}: {TrimLabel(label)}",
                Math.Clamp(location.Item1, 0, preview.Width - 20),
                Math.Clamp(location.Item2, 0, preview.Height - 20),
                Math.Clamp(size.Item1, 40, preview.Width),
                Math.Clamp(size.Item2, 24, preview.Height)));
            index++;
        }
        return elements.Count == 0
            ? [new("form", "form", "WinForms source (no supported controls detected)", 30, 30, Math.Max(180, preview.Width - 60), 90)]
            : elements;
    }

    private static IReadOnlyList<PreviewDesignElement> AnalyzeObsComponent(
        FoundryWorkspace workspace,
        FoundryPreview preview)
    {
        var design = workspace.Manifest.ObsPlugin!.Design!;
        var width = Math.Max(240, preview.Width * 0.72);
        var height = Math.Max(140, preview.Height * 0.55);
        return
        [
            new("obs-canvas", design.ComponentId, design.ComponentName,
                (preview.Width - width) / 2, (preview.Height - height) / 2, width, height),
            new("obs-template", design.Template, $"Template: {design.Template}",
                24, 24, Math.Min(420, preview.Width - 48), 48),
        ];
    }

    private static (int, int)? ReadPair(string source, string name, string property)
    {
        var match = Regex.Match(
            source,
            $@"\b{Regex.Escape(name)}\.{property}\s*=\s*new\s+(?:System\.Drawing\.)?(?:Point|Size)\s*\(\s*(?<a>\d+)\s*,\s*(?<b>\d+)\s*\)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));
        return match.Success
            ? (int.Parse(match.Groups["a"].Value, System.Globalization.CultureInfo.InvariantCulture),
               int.Parse(match.Groups["b"].Value, System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    private static string? ReadText(string source, string name)
    {
        var match = Regex.Match(
            source,
            $"\\b{Regex.Escape(name)}\\.Text\\s*=\\s*\"(?<text>[^\"]{{0,160}})\"",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));
        return match.Success ? match.Groups["text"].Value : null;
    }

    private static string TrimLabel(string value) =>
        value.Length <= 80 ? value : $"{value[..77]}...";

    private static IEnumerable<string> EnumerateFiles(IEnumerable<ProjectTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory) yield return node.RelativePath.Replace('\\', '/');
            foreach (var child in EnumerateFiles(node.Children)) yield return child;
        }
    }

    private static string? ResolveOwnedPath(string projectRoot, string relativePath)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return candidate.StartsWith($"{root}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static FoundryDiagnostic Error(string code, string message, string path, string fix) =>
        new(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(path), fix);

    private static WorkspaceOperationResult<PreviewDesignSurface> Failure(
        string code,
        string message,
        string path,
        string fix) => new(null, [Error(code, message, path, fix)]);
}
