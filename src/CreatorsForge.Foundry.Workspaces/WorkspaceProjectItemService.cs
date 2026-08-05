using System.Text;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Workspaces;

public static class WorkspaceProjectItemService
{
    private static readonly Dictionary<WorkspaceProjectItemKind, (string Extension, string Content)> Templates =
        new Dictionary<WorkspaceProjectItemKind, (string, string)>
        {
            [WorkspaceProjectItemKind.CSharp] = (".cs", string.Empty),
            [WorkspaceProjectItemKind.Cpp] = (".cpp", string.Empty),
            [WorkspaceProjectItemKind.C] = (".c", string.Empty),
            [WorkspaceProjectItemKind.Header] = (".h", "#pragma once\n"),
            [WorkspaceProjectItemKind.Json] = (".json", "{}\n"),
            [WorkspaceProjectItemKind.Xml] = (".xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root />\n"),
            [WorkspaceProjectItemKind.Html] = (".html", "<!doctype html>\n<html lang=\"en\">\n<head>\n  <meta charset=\"utf-8\">\n  <title>Foundry</title>\n</head>\n<body>\n</body>\n</html>\n"),
            [WorkspaceProjectItemKind.Css] = (".css", string.Empty),
            [WorkspaceProjectItemKind.JavaScript] = (".js", string.Empty),
            [WorkspaceProjectItemKind.Markdown] = (".md", string.Empty),
            [WorkspaceProjectItemKind.Text] = (".txt", string.Empty),
            [WorkspaceProjectItemKind.CMake] = (".txt", "cmake_minimum_required(VERSION 3.28)\n"),
        };

    public static async Task<WorkspaceOperationResult<WorkspaceProjectItem>> CreateAsync(
        string projectRoot,
        string parentDirectory,
        string name,
        WorkspaceProjectItemKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveParent(projectRoot, parentDirectory, out var fullRoot, out var fullParent))
        {
            return Failure("CFW1101", "The new item must be created inside the project directory.", parentDirectory);
        }

        var trimmedName = name.Trim();
        if (!IsValidName(trimmedName))
        {
            return Failure("CFW1102", "Enter one valid file or folder name without a path.", name);
        }

        (string Extension, string Content) template = (string.Empty, string.Empty);
        if (kind != WorkspaceProjectItemKind.Folder && !Templates.TryGetValue(kind, out template))
        {
            return Failure("CFW1105", "The requested project item type is not supported.", name);
        }

        if (kind == WorkspaceProjectItemKind.CMake &&
            string.Equals(trimmedName, "CMakeLists", StringComparison.OrdinalIgnoreCase))
        {
            trimmedName = "CMakeLists.txt";
        }
        else if (kind != WorkspaceProjectItemKind.Folder &&
                 string.IsNullOrEmpty(Path.GetExtension(trimmedName)))
        {
            trimmedName += template.Extension;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(fullParent, trimmedName));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            return Failure("CFW1102", $"The project item name is invalid: {exception.Message}", name);
        }

        if (!IsWithin(fullRoot, fullPath))
        {
            return Failure("CFW1101", "The new item must be created inside the project directory.", fullPath);
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return Failure("CFW1103", "A file or folder with that name already exists.", fullPath);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kind == WorkspaceProjectItemKind.Folder)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                var content = template.Content;
                await using var stream = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return new(
                new(
                    fullPath,
                    Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/'),
                    kind == WorkspaceProjectItemKind.Folder),
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("CFW1104", $"The project item could not be created: {exception.Message}", fullPath);
        }
    }

    private static bool TryResolveParent(
        string projectRoot,
        string parentDirectory,
        out string fullRoot,
        out string fullParent)
    {
        try
        {
            fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
            fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDirectory));
            return Directory.Exists(fullParent) &&
                   (string.Equals(fullRoot, fullParent, StringComparison.OrdinalIgnoreCase) ||
                    IsWithin(fullRoot, fullParent)) &&
                   !ContainsReparsePoint(fullRoot, fullParent);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or PathTooLongException or UnauthorizedAccessException)
        {
            fullRoot = projectRoot;
            fullParent = parentDirectory;
            return false;
        }
    }

    private static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.Contains(Path.DirectorySeparatorChar) &&
        !name.Contains(Path.AltDirectorySeparatorChar);

    private static bool IsWithin(string root, string candidate) =>
        candidate.StartsWith($"{root}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsReparsePoint(string root, string candidate)
    {
        var current = candidate;
        while (current.Length >= root.Length)
        {
            if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current) ?? root;
        }

        return false;
    }

    private static WorkspaceOperationResult<WorkspaceProjectItem> Failure(string code, string message, string path) =>
        new(
            null,
            [new FoundryDiagnostic(
                code,
                FoundryDiagnosticSeverity.Error,
                message,
                new FoundryDiagnosticLocation(path))]);
}
