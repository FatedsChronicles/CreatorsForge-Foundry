using System.Text;

namespace CreatorsForge.Foundry.Workspaces;

internal static class AtomicFile
{
    private static readonly Encoding Utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteTextAsync(
        string destinationPath,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath) ??
            throw new ArgumentException(
                "The destination has no parent directory.",
                nameof(destinationPath));
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                Utf8NoBom,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Cleanup must not replace the original persistence result.
                }
                catch (UnauthorizedAccessException)
                {
                    // Cleanup must not replace the original persistence result.
                }
            }
        }
    }
}
