using System.ComponentModel;
using System.Text;
using System.Windows.Media;
using CreatorsForge.Foundry.Editor;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace CreatorsForge.Foundry.App;

internal sealed class CphCompletionData : ICompletionData
{
    private readonly CphCompletionItem item;

    public CphCompletionData(CphCompletionItem item)
    {
        this.item = item;
    }

    public ImageSource? Image => null;

    public string Text => item.Name;

    public object Content => $"{item.Name}  [{item.Category}]";

    public object Description => BuildDescription();

    public double Priority => string.Equals(
        item.Status,
        "deprecated",
        StringComparison.OrdinalIgnoreCase)
        ? -1
        : 0;

    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, Text);

    private string BuildDescription()
    {
        var builder = new StringBuilder();
        foreach (var overload in item.Overloads)
        {
            builder.AppendLine(overload.Signature);
        }

        builder.AppendLine().AppendLine(item.Summary);
        builder.Append("Available: ").AppendLine(item.Availability);
        if (string.Equals(
            item.Status,
            "deprecated",
            StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("DEPRECATED");
        }

        if (!string.IsNullOrWhiteSpace(item.Example))
        {
            builder.AppendLine().Append("Example: ").AppendLine(item.Example);
        }

        if (!string.IsNullOrWhiteSpace(item.DocumentationUrl))
        {
            builder.AppendLine().Append("Reference: ").Append(item.DocumentationUrl);
        }

        return builder.ToString();
    }
}

internal sealed class CphOverloadProvider : IOverloadProvider
{
    private readonly CphSignatureHelp signatureHelp;
    private int selectedIndex;

    public CphOverloadProvider(CphSignatureHelp signatureHelp)
    {
        this.signatureHelp = signatureHelp;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            var bounded = Math.Clamp(value, 0, Math.Max(0, Count - 1));
            if (selectedIndex == bounded)
            {
                return;
            }

            selectedIndex = bounded;
            PropertyChanged?.Invoke(
                this,
                new(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(
                this,
                new(nameof(CurrentIndexText)));
            PropertyChanged?.Invoke(
                this,
                new(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(
                this,
                new(nameof(CurrentContent)));
        }
    }

    public int Count => signatureHelp.Overloads.Count;

    public string CurrentIndexText => $"{SelectedIndex + 1} of {Count}";

    public object CurrentHeader =>
        signatureHelp.Overloads[SelectedIndex].Signature;

    public object CurrentContent
    {
        get
        {
            var overload = signatureHelp.Overloads[SelectedIndex];
            var active = signatureHelp.ActiveParameter < overload.Parameters.Count
                ? overload.Parameters[signatureHelp.ActiveParameter]
                : null;
            return active is null
                ? $"{signatureHelp.Summary}\nAvailable: {signatureHelp.Availability}"
                : $"{active.Name}: {active.Description}\n\n" +
                  $"{signatureHelp.Summary}\nAvailable: {signatureHelp.Availability}";
        }
    }
}

internal sealed class SnippetCompletionData : ICompletionData
{
    private readonly SnippetCompletionItem item;
    private readonly ISnippetService snippets;
    private readonly Action<int, SnippetExpansion> inserted;
    private readonly string filterText;

    public SnippetCompletionData(
        SnippetCompletionItem item,
        ISnippetService snippets,
        int caretOffset,
        Action<int, SnippetExpansion> inserted)
    {
        this.item = item;
        this.snippets = snippets;
        this.inserted = inserted;
        var typedLength = Math.Clamp(
            caretOffset - item.ReplacementStart,
            0,
            item.Prefix.Length);
        filterText = item.Prefix[typedLength..];
        if (filterText.Length == 0)
        {
            filterText = item.Prefix;
        }
    }

    public ImageSource? Image => null;

    public string Text => filterText;

    public object Content => $"{item.Prefix}  [{item.Kind} snippet]";

    public object Description
    {
        get
        {
            var security = item.Security is
                {
                    FileAccess: false,
                    NetworkAccess: false,
                    ProcessExecution: false,
                }
                ? "No file, network, or process access declared."
                : "Declares security-relevant capabilities.";
            return $"{item.Name}\n\n{item.Description}\n\n" +
                $"Available: {item.Availability}\n" +
                $"Source: {item.Source}\n{security}";
        }
    }

    public double Priority => 1;

    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs)
    {
        var document = textArea.Document;
        var start = Math.Clamp(
            item.ReplacementStart,
            0,
            document.TextLength);
        var end = Math.Clamp(
            completionSegment.EndOffset,
            start,
            document.TextLength);
        var line = document.GetLineByOffset(start);
        var linePrefix = document.GetText(line.Offset, start - line.Offset);
        var indentation = new string(
            linePrefix.TakeWhile(char.IsWhiteSpace).ToArray());
        var newLine = document.Text.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var expansion = snippets.Expand(
            item.Id,
            indentation,
            newLine);
        document.Replace(start, end - start, expansion.Text);
        inserted(start, expansion);
    }
}

internal sealed class ObsNativeCompletionData : ICompletionData
{
    private readonly ObsNativeCompletionItem item;

    public ObsNativeCompletionData(ObsNativeCompletionItem item) => this.item = item;

    public ImageSource? Image => null;

    public string Text => item.Name;

    public object Content => $"{item.Name}  [{item.Kind} · {item.Category}]";

    public object Description
    {
        get
        {
            var reference = string.IsNullOrWhiteSpace(item.DocumentationUrl)
                ? string.Empty
                : $"\nOfficial reference: {item.DocumentationUrl}";
            return $"{item.Signature}\n\n{item.Summary}\n\n" +
                $"Header: <{item.Header}>\nAvailable: {item.Availability}{reference}";
        }
    }

    public double Priority => 0;

    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs)
    {
        var start = Math.Clamp(item.ReplacementStart, 0, textArea.Document.TextLength);
        var end = Math.Clamp(completionSegment.EndOffset, start, textArea.Document.TextLength);
        textArea.Document.Replace(start, end - start, item.Name);
    }
}

internal sealed class ObsNativeOverloadProvider : IOverloadProvider
{
    private readonly ObsNativeSignatureHelp signatureHelp;

    public ObsNativeOverloadProvider(ObsNativeSignatureHelp signatureHelp) =>
        this.signatureHelp = signatureHelp;

#pragma warning disable CS0067
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    public int SelectedIndex { get => 0; set { } }

    public int Count => 1;

    public string CurrentIndexText => "1 of 1";

    public object CurrentHeader => signatureHelp.Symbol.Signature;

    public object CurrentContent
    {
        get
        {
            var symbol = signatureHelp.Symbol;
            var active = signatureHelp.ActiveParameter < symbol.Parameters.Count
                ? symbol.Parameters[signatureHelp.ActiveParameter]
                : null;
            var parameter = active is null
                ? string.Empty
                : $"{active.Name}: {active.Description}\n\n";
            return $"{parameter}{symbol.Summary}\nHeader: <{symbol.Header}>";
        }
    }
}
