namespace CreatorsForge.Foundry.App;

public sealed class DocumentViewModel : ObservableObject
{
    private string text;
    private bool isDirty;

    public DocumentViewModel(
        string fullPath,
        string relativePath,
        string initialText,
        DateTimeOffset lastWriteUtc,
        string targetProfile,
        bool isReadOnly = false)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        text = initialText;
        LastWriteUtc = lastWriteUtc;
        TargetProfile = targetProfile;
        IsReadOnly = isReadOnly;
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public string FileName => Path.GetFileName(FullPath);

    public string Title => IsDirty ? $"{FileName} *" : FileName;

    public DateTimeOffset LastWriteUtc { get; private set; }

    public string TargetProfile { get; }

    public bool IsReadOnly { get; }

    public string Text
    {
        get => text;
        set
        {
            if (!IsReadOnly && SetProperty(ref text, value))
            {
                IsDirty = true;
            }
        }
    }

    public bool IsDirty
    {
        get => isDirty;
        private set
        {
            if (SetProperty(ref isDirty, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public void Restore(string recoveredText)
    {
        text = recoveredText;
        OnPropertyChanged(nameof(Text));
        IsDirty = true;
    }

    public void Reload(string persistedText, DateTimeOffset lastWriteUtc)
    {
        text = persistedText;
        LastWriteUtc = lastWriteUtc;
        OnPropertyChanged(nameof(Text));
        IsDirty = false;
    }

    public void MarkSaved(DateTimeOffset lastWriteUtc)
    {
        LastWriteUtc = lastWriteUtc;
        IsDirty = false;
    }
}
