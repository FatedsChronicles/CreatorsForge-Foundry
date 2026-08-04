using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace CreatorsForge.Foundry.StreamerBot.ExportInspector;

public static class ExportEnvelope
{
    public const int MaxImportCodeCharacters = 16 * 1024 * 1024;
    public const int MaxDecodedJsonBytes = 64 * 1024 * 1024;

    private static readonly byte[] Magic = "SBAE"u8.ToArray();

    public static DecodedExport Decode(string importCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importCode);

        if (importCode.Length > MaxImportCodeCharacters)
        {
            throw new InvalidDataException(
                $"The import code exceeds the {MaxImportCodeCharacters}-character safety limit.");
        }

        string compactImportCode = RemoveWhitespace(importCode);
        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(compactImportCode);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "The Streamer.bot import code is not valid Base64 text.",
                exception);
        }

        if (envelope.Length <= Magic.Length
            || !envelope.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "The import code does not begin with the expected SBAE envelope signature.");
        }

        using var compressed = new MemoryStream(
            envelope,
            Magic.Length,
            envelope.Length - Magic.Length,
            writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream();

        try
        {
            CopyWithLimit(gzip, decoded, MaxDecodedJsonBytes);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                "The SBAE payload is not a valid GZip stream.",
                exception);
        }

        byte[] jsonBytes = decoded.ToArray();
        string json = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(jsonBytes);
        JsonNode root = JsonNode.Parse(json)
            ?? throw new InvalidDataException("The decoded export contains empty JSON.");

        return new DecodedExport(
            envelope.Length,
            jsonBytes.Length,
            json,
            root);
    }

    private static void CopyWithLimit(Stream source, Stream destination, int byteLimit)
    {
        byte[] buffer = new byte[81920];
        int totalBytes = 0;
        int bytesRead;

        while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > byteLimit)
            {
                throw new InvalidDataException(
                    $"The decoded export exceeds the {byteLimit}-byte safety limit.");
            }

            destination.Write(buffer, 0, bytesRead);
        }
    }

    private static string RemoveWhitespace(string value)
    {
        return string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
    }
}

public sealed record DecodedExport(
    int EnvelopeBytes,
    int JsonBytes,
    string Json,
    JsonNode Root);
