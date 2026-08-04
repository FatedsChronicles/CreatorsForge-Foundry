using System.Text.Json;

namespace CreatorsForge.Foundry.Build;

internal static class CphCatalogueMetadata
{
    private static readonly Lazy<string> RevisionValue = new(LoadRevision);

    public static string Revision => RevisionValue.Value;

    private static string LoadRevision()
    {
        var assembly = typeof(CphCatalogueMetadata).Assembly;
        const string resourceName =
            "CreatorsForge.Foundry.Build.Catalogs.streamerbot-cph-v1.json";
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded CPH catalogue '{resourceName}' was not found.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("revision").GetString() ??
            throw new InvalidOperationException(
                "The embedded CPH catalogue has no revision.");
    }
}
