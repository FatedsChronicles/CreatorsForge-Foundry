namespace CreatorsForge.Foundry.Workspaces;

public sealed class TerminalCommandHistory
{
    private const int MaximumEntries = 100;
    private readonly List<string> entries = [];
    private int position;

    public int Count => entries.Count;

    public void Record(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (entries.Count == 0 ||
            !string.Equals(entries[^1], command, StringComparison.Ordinal))
        {
            entries.Add(command);
            if (entries.Count > MaximumEntries)
            {
                entries.RemoveAt(0);
            }
        }

        position = entries.Count;
    }

    public string? Previous()
    {
        if (entries.Count == 0)
        {
            return null;
        }

        position = Math.Max(0, position - 1);
        return entries[position];
    }

    public string? Next()
    {
        if (entries.Count == 0)
        {
            return null;
        }

        position = Math.Min(entries.Count, position + 1);
        return position == entries.Count ? string.Empty : entries[position];
    }
}
