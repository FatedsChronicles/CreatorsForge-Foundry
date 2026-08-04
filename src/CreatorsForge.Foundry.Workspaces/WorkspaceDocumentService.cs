using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Workspaces;

public static class WorkspaceDocumentService
{
    public const long MaximumDocumentBytes = 4 * 1024 * 1024;

    public static async Task<WorkspaceOperationResult<WorkspaceDocument>> LoadAsync(
        string projectRoot,
        string documentPath,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePath(projectRoot, documentPath, out var fullPath))
        {
            return Failure(
                "CFW1001",
                "The document path must remain inside the project directory.",
                documentPath);
        }

        if (!File.Exists(fullPath))
        {
            return Failure("CFW1002", "The document does not exist.", fullPath);
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > MaximumDocumentBytes)
            {
                return Failure(
                    "CFW1003",
                    $"The document exceeds the {MaximumDocumentBytes} byte editor limit.",
                    fullPath);
            }

            var text = await File.ReadAllTextAsync(
                fullPath,
                cancellationToken).ConfigureAwait(false);
            return new(
                new(
                    fullPath,
                    Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/'),
                    text,
                    fileInfo.LastWriteTimeUtc),
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "CFW1004",
                $"The document could not be read: {exception.Message}",
                fullPath);
        }
    }

    public static async Task<WorkspaceOperationResult<WorkspaceDocument>> SaveAsync(
        string projectRoot,
        string documentPath,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryResolvePath(projectRoot, documentPath, out var fullPath))
        {
            return Failure(
                "CFW1001",
                "The document path must remain inside the project directory.",
                documentPath);
        }

        try
        {
            await AtomicFile.WriteTextAsync(
                fullPath,
                text,
                cancellationToken).ConfigureAwait(false);
            var fileInfo = new FileInfo(fullPath);
            return new(
                new(
                    fullPath,
                    Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/'),
                    text,
                    fileInfo.LastWriteTimeUtc),
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "CFW1005",
                $"The document could not be saved: {exception.Message}",
                fullPath);
        }
    }

    private static bool TryResolvePath(
        string projectRoot,
        string documentPath,
        out string fullPath)
    {
        try
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(projectRoot));
            fullPath = Path.GetFullPath(documentPath);
            var isInsideRoot = fullPath.StartsWith(
                $"{fullRoot}{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase);
            return isInsideRoot && !ContainsReparsePoint(fullRoot, fullPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            fullPath = documentPath;
            return false;
        }
    }

    private static bool ContainsReparsePoint(string root, string candidate)
    {
        var current = candidate;
        while (current.Length >= root.Length)
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (string.Equals(
                Path.TrimEndingDirectorySeparator(current),
                root,
                StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    private static WorkspaceOperationResult<WorkspaceDocument> Failure(
        string code,
        string message,
        string path)
    {
        var diagnostic = new FoundryDiagnostic(
            code,
            FoundryDiagnosticSeverity.Error,
            message,
            new FoundryDiagnosticLocation(path));
        return new(null, [diagnostic]);
    }
}
