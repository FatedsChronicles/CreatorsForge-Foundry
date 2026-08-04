using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CreatorsForge.Foundry.Editor;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class CodeEditor : UserControl
{
    private DocumentViewModel? document;
    private bool isSynchronizing;
    private CompletionWindow? completionWindow;
    private OverloadInsightWindow? insightWindow;
    private SnippetPlaceholderSession? snippetSession;
    private bool completionContainsSnippets;

    public CodeEditor()
    {
        InitializeComponent();
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextEntering += TextArea_TextEntering;
        Editor.TextArea.TextEntered += TextArea_TextEntered;
    }

    public event EventHandler<EditorPositionEventArgs>? DefinitionRequested;

    public event EventHandler? FormatRequested;

    public void NavigateTo(int line, int column)
    {
        if (Editor.Document.LineCount == 0)
        {
            return;
        }

        var boundedLine = Math.Clamp(line, 1, Editor.Document.LineCount);
        var documentLine = Editor.Document.GetLineByNumber(boundedLine);
        var boundedColumn = Math.Clamp(column, 1, documentLine.Length + 1);
        Editor.TextArea.Caret.Line = boundedLine;
        Editor.TextArea.Caret.Column = boundedColumn;
        Editor.ScrollTo(boundedLine, boundedColumn);
        Editor.Focus();
    }

    public bool InsertGuidedSnippet(
        SnippetService snippets,
        string snippetId,
        IReadOnlyDictionary<int, string> values)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        if (document is null)
        {
            return false;
        }

        var start = Editor.SelectionLength == 0
            ? Editor.CaretOffset
            : Editor.SelectionStart;
        var line = Editor.Document.GetLineByOffset(start);
        var linePrefix = Editor.Document.GetText(
            line.Offset,
            start - line.Offset);
        var indentation = new string(
            linePrefix.TakeWhile(char.IsWhiteSpace).ToArray());
        var newLine = Editor.Document.Text.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var result = snippets.ExpandGuided(
            snippetId,
            values,
            indentation,
            newLine);
        if (!result.IsSuccess)
        {
            return false;
        }

        completionWindow?.Close();
        insightWindow?.Close();
        Editor.Document.Replace(
            start,
            Editor.SelectionLength,
            result.Expansion!.Text);
        StartSnippetSession(start, result.Expansion);
        return true;
    }

    private void CodeEditor_Loaded(object sender, RoutedEventArgs e)
    {
        AttachDocument(DataContext as DocumentViewModel);
        if (Application.Current is App app)
        {
            app.ThemeChanged -= App_ThemeChanged;
            app.ThemeChanged += App_ThemeChanged;
        }

        ApplyEditorTheme();
    }

    private void CodeEditor_Unloaded(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ThemeChanged -= App_ThemeChanged;
        }

        CloseEditorWindows();
    }

    private void CodeEditor_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        AttachDocument(e.NewValue as DocumentViewModel);

    private void AttachDocument(DocumentViewModel? value)
    {
        if (ReferenceEquals(document, value))
        {
            return;
        }

        if (document is not null)
        {
            document.PropertyChanged -= Document_PropertyChanged;
        }

        document = value;
        if (document is not null)
        {
            document.PropertyChanged += Document_PropertyChanged;
            ApplyEditorTheme();
            Editor.IsReadOnly = document.IsReadOnly;
            SetEditorText(document.Text);
        }
    }

    private void App_ThemeChanged(object? sender, EventArgs e) => ApplyEditorTheme();

    private void ApplyEditorTheme()
    {
        if (document is null || Application.Current is not App app)
        {
            return;
        }

        Editor.SyntaxHighlighting = App.IsHighContrast
            ? null
            : app.EffectiveTheme == FoundryThemePreference.Dark
                ? FoundrySyntaxHighlighting.Dark
                : ICSharpCode.AvalonEdit.Highlighting.HighlightingManager
                    .Instance.GetDefinition(
                        string.Equals(
                            Path.GetExtension(document.FullPath),
                            ".cs",
                            StringComparison.OrdinalIgnoreCase)
                            ? "C#"
                            : "C++");
        Editor.TextArea.SelectionBrush =
            (System.Windows.Media.Brush)FindResource("MenuSelectionBrush");
        Editor.TextArea.SelectionForeground =
            (System.Windows.Media.Brush)FindResource("TextBrush");
        Editor.TextArea.TextView.CurrentLineBackground =
            (System.Windows.Media.Brush)FindResource("PanelBackgroundBrush");
    }

    private void Document_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.Text) &&
            document is not null &&
            !string.Equals(Editor.Text, document.Text, StringComparison.Ordinal))
        {
            var caretOffset = Editor.CaretOffset;
            try
            {
                isSynchronizing = true;
                Editor.Document.Replace(
                    0,
                    Editor.Document.TextLength,
                    document.Text);
            }
            finally
            {
                isSynchronizing = false;
            }

            Editor.CaretOffset = Math.Min(caretOffset, Editor.Document.TextLength);
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (!isSynchronizing && document is not null)
        {
            document.Text = Editor.Text;
        }
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab &&
            completionWindow is null &&
            NavigateSnippetPlaceholders(
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && snippetSession is not null)
        {
            snippetSession = null;
        }
        else if (e.Key == Key.F12)
        {
            e.Handled = true;
            DefinitionRequested?.Invoke(
                this,
                new(document, Editor.CaretOffset));
        }
        else if (e.Key == Key.F &&
                 Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            e.Handled = true;
            FormatRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == Key.Space &&
                 Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ShowCompletion();
        }
    }

    private void TextArea_TextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (completionWindow is not null &&
            e.Text.Length > 0 &&
            !char.IsLetterOrDigit(e.Text[0]) &&
            e.Text[0] != '_' &&
            !(completionContainsSnippets && e.Text[0] == '.'))
        {
            completionWindow.CompletionList.RequestInsertion(e);
        }
    }

    private void TextArea_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text == ".")
        {
            ShowCompletion();
        }
        else if (e.Text is "(" or ",")
        {
            ShowSignatureHelp();
        }
        else if (e.Text == ")")
        {
            insightWindow?.Close();
        }
        else if (IsNativeDocument() &&
                 completionWindow is null &&
                 EndsWithNativeCompletionPrefix())
        {
            ShowCompletion();
        }
    }

    private void ShowCompletion()
    {
        if (document is null)
        {
            return;
        }

        if (IsNativeDocument())
        {
            ShowNativeCompletion();
            return;
        }

        if (!string.Equals(
            Path.GetExtension(document.FullPath),
            ".cs",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var items = CphIntelligenceProvider.Default.GetCompletions(
            Editor.Text,
            Editor.CaretOffset,
            document.TargetProfile);
        if (items.Count != 0)
        {
            completionWindow?.Close();
            completionContainsSnippets = false;
            completionWindow = new(Editor.TextArea);
            foreach (var item in items)
            {
                completionWindow.CompletionList.CompletionData.Add(
                    new CphCompletionData(item));
            }

            completionWindow.Closed += (_, _) =>
            {
                completionWindow = null;
                completionContainsSnippets = false;
            };
            completionWindow.Show();
            return;
        }

        var snippetItems = SnippetProvider.Default.GetCompletions(
            Editor.Text,
            Editor.CaretOffset,
            document.TargetProfile);
        if (snippetItems.Count == 0)
        {
            return;
        }

        completionWindow?.Close();
        completionContainsSnippets = true;
        completionWindow = new(Editor.TextArea);
        foreach (var item in snippetItems)
        {
            completionWindow.CompletionList.CompletionData.Add(
                new SnippetCompletionData(
                    item,
                    SnippetProvider.Default,
                    Editor.CaretOffset,
                    StartSnippetSession));
        }

        completionWindow.Closed += (_, _) =>
        {
            completionWindow = null;
            completionContainsSnippets = false;
        };
        completionWindow.Show();
    }

    private void ShowSignatureHelp()
    {
        if (document is null)
        {
            return;
        }

        if (IsNativeDocument())
        {
            ShowNativeSignatureHelp();
            return;
        }

        if (!string.Equals(
            Path.GetExtension(document.FullPath),
            ".cs",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var help = CphIntelligenceProvider.Default.GetSignatureHelp(
            Editor.Text,
            Editor.CaretOffset,
            document.TargetProfile);
        if (help is null)
        {
            return;
        }

        insightWindow?.Close();
        insightWindow = new(Editor.TextArea)
        {
            Provider = new CphOverloadProvider(help),
        };
        insightWindow.Closed += (_, _) => insightWindow = null;
        insightWindow.Show();
    }

    private void ShowNativeCompletion()
    {
        if (document is null)
        {
            return;
        }

        var items = ObsNativeIntelligenceProvider.Default.GetCompletions(
            Editor.Text,
            Editor.CaretOffset,
            document.TargetProfile);
        if (items.Count == 0)
        {
            return;
        }

        completionWindow?.Close();
        completionContainsSnippets = false;
        completionWindow = new(Editor.TextArea);
        foreach (var item in items)
        {
            completionWindow.CompletionList.CompletionData.Add(
                new ObsNativeCompletionData(item));
        }

        completionWindow.Closed += (_, _) => completionWindow = null;
        completionWindow.Show();
    }

    private void ShowNativeSignatureHelp()
    {
        if (document is null)
        {
            return;
        }

        var help = ObsNativeIntelligenceProvider.Default.GetSignatureHelp(
            Editor.Text,
            Editor.CaretOffset,
            document.TargetProfile);
        if (help is null)
        {
            return;
        }

        insightWindow?.Close();
        insightWindow = new(Editor.TextArea)
        {
            Provider = new ObsNativeOverloadProvider(help),
        };
        insightWindow.Closed += (_, _) => insightWindow = null;
        insightWindow.Show();
    }

    private bool IsNativeDocument()
    {
        var extension = Path.GetExtension(document?.FullPath);
        return string.Equals(extension, ".c", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".h", StringComparison.OrdinalIgnoreCase);
    }

    private bool EndsWithNativeCompletionPrefix()
    {
        var bounded = Math.Clamp(Editor.CaretOffset, 0, Editor.Text.Length);
        var start = bounded;
        while (start > 0 &&
               (char.IsLetterOrDigit(Editor.Text[start - 1]) || Editor.Text[start - 1] == '_'))
        {
            start--;
        }

        var identifier = Editor.Text[start..bounded];
        return identifier.Length >= 4 &&
            (identifier.StartsWith("obs_", StringComparison.OrdinalIgnoreCase) ||
             identifier.StartsWith("OBS_", StringComparison.Ordinal));
    }

    private void CloseEditorWindows()
    {
        completionWindow?.Close();
        insightWindow?.Close();
        snippetSession = null;
        AttachDocument(null);
    }

    private void SetEditorText(string text)
    {
        snippetSession = null;
        try
        {
            isSynchronizing = true;
            Editor.Text = text;
        }
        finally
        {
            isSynchronizing = false;
        }
    }

    private void StartSnippetSession(
        int insertionOffset,
        SnippetExpansion expansion)
    {
        snippetSession = new(
            Editor.Document,
            insertionOffset,
            expansion.Placeholders);
        _ = NavigateSnippetPlaceholders(reverse: false);
    }

    private bool NavigateSnippetPlaceholders(bool reverse)
    {
        if (snippetSession is null)
        {
            return false;
        }

        if (!snippetSession.TryMove(reverse, out var offset, out var length))
        {
            snippetSession = null;
            return true;
        }

        Editor.Select(offset, length);
        Editor.ScrollToLine(Editor.Document.GetLineByOffset(offset).LineNumber);
        Editor.Focus();
        return true;
    }

    private sealed class SnippetPlaceholderSession
    {
        private readonly TextDocument document;
        private readonly PlaceholderAnchor[] placeholders;
        private int current = -1;

        public SnippetPlaceholderSession(
            TextDocument document,
            int insertionOffset,
            IReadOnlyList<SnippetPlaceholder> placeholders)
        {
            this.document = document;
            this.placeholders = placeholders
                .Select(placeholder => CreateAnchor(
                    document,
                    insertionOffset,
                    placeholder))
                .ToArray();
        }

        public bool TryMove(
            bool reverse,
            out int offset,
            out int length)
        {
            var next = reverse ? current - 1 : current + 1;
            if (next < 0 || next >= placeholders.Length)
            {
                offset = 0;
                length = 0;
                return false;
            }

            current = next;
            var placeholder = placeholders[current];
            if (placeholder.Start.IsDeleted || placeholder.End.IsDeleted)
            {
                offset = 0;
                length = 0;
                return false;
            }

            offset = Math.Clamp(
                placeholder.Start.Offset,
                0,
                document.TextLength);
            var end = Math.Clamp(
                placeholder.End.Offset,
                offset,
                document.TextLength);
            length = end - offset;
            return true;
        }

        private static PlaceholderAnchor CreateAnchor(
            TextDocument document,
            int insertionOffset,
            SnippetPlaceholder placeholder)
        {
            var start = document.CreateAnchor(
                insertionOffset + placeholder.Offset);
            start.MovementType = AnchorMovementType.BeforeInsertion;
            start.SurviveDeletion = true;
            var end = document.CreateAnchor(
                insertionOffset + placeholder.Offset + placeholder.Length);
            end.MovementType = AnchorMovementType.AfterInsertion;
            end.SurviveDeletion = true;
            return new(start, end);
        }

        private sealed record PlaceholderAnchor(
            TextAnchor Start,
            TextAnchor End);
    }
}

public sealed class EditorPositionEventArgs : EventArgs
{
    public EditorPositionEventArgs(DocumentViewModel? document, int offset)
    {
        Document = document;
        Offset = offset;
    }

    public DocumentViewModel? Document { get; }

    public int Offset { get; }
}
