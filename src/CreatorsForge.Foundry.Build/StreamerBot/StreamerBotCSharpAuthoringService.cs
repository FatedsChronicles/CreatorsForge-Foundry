using System.Security.Cryptography;
using System.Text;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public sealed record StreamerBotCSharpConversionPreview(
    StreamerBotSubAction ConvertedSubAction,
    string Source,
    string RelativePath,
    string Summary);

public enum StreamerBotGeneratedSourceState
{
    Manual,
    Generated,
    Detached,
    Missing,
}

/// <summary>
/// Produces editable Execute C# source as inert text. This service never loads,
/// compiles, or executes project code.
/// </summary>
public static class StreamerBotCSharpAuthoringService
{
    public const string SetArgumentRevision = "set-argument-csharp-v1";
    public const string ManualRevision = "manual-csharp-v1";
    public const int MaximumSourceBytes = 1024 * 1024;

    public static StreamerBotCSharpConversionPreview PreviewSetArgumentConversion(
        StreamerBotSubAction source,
        string actionId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (source.ReadOnly)
            throw new InvalidOperationException("Read-only preserved sub-actions cannot be converted.");
        if (source.Kind != "setArgument")
            throw new InvalidOperationException("Only a verified Set Argument sub-action can be converted in this increment.");
        if (source.AutoType)
            throw new InvalidOperationException("Auto Type conversion is blocked because Streamer.bot's native coercion semantics are not yet verified as equivalent to C#.");
        if (string.IsNullOrWhiteSpace(source.VariableName))
            throw new InvalidOperationException("Set Argument requires a variable name before conversion.");

        var generatedSource = CreateSetArgumentSource(source.VariableName, source.Value ?? string.Empty);
        var relativePath = CreateRelativePath(actionId, source.Id);
        var generation = new StreamerBotCSharpGeneration(
            SetArgumentRevision,
            "setArgument",
            source.Id,
            Sha256(generatedSource));
        var converted = source with
        {
            Kind = "executeCSharp",
            VariableName = null,
            Value = null,
            AutoType = false,
            SourcePath = relativePath,
            SourceType = 99999,
            References = [],
            Generation = generation,
            DetachedFromGenerator = false,
        };
        return new(converted, generatedSource, relativePath,
            $"Convert Set Argument '{source.Id}' to editable Execute C# Code using {SetArgumentRevision}.");
    }

    public static (StreamerBotSubAction SubAction, string Source) CreateManual(
        string id,
        string actionId,
        bool enabled = true,
        double weight = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        const string source =
            "using System;\n\n" +
            "public class CPHInline\n" +
            "{\n" +
            "\tpublic bool Execute()\n" +
            "\t{\n" +
            "\t\t// your main code goes here\n" +
            "\t\treturn true;\n" +
            "\t}\n" +
            "}\n";
        return (new StreamerBotSubAction(
            id, "executeCSharp", enabled, null, null, false,
            CreateRelativePath(actionId, id), 99999, References: [], Weight: weight), source);
    }

    public static StreamerBotGeneratedSourceState GetState(
        StreamerBotSubAction subAction,
        string? source)
    {
        if (source is null) return StreamerBotGeneratedSourceState.Missing;
        if (subAction.Generation is null) return StreamerBotGeneratedSourceState.Manual;
        return subAction.DetachedFromGenerator ||
               !string.Equals(Sha256(source), subAction.Generation.SourceSha256,
                   StringComparison.OrdinalIgnoreCase)
            ? StreamerBotGeneratedSourceState.Detached
            : StreamerBotGeneratedSourceState.Generated;
    }

    public static string CreateRelativePath(string actionId, string subActionId) =>
        $"streamerbot/code/{SafeSegment(actionId)}/{SafeSegment(subActionId)}.cs";

    public static string ResolveConfinedSourcePath(string projectRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var path = Path.GetFullPath(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Execute C# source path leaves the project or is not a .cs file.");
        return path;
    }

    public static void WriteNewSource(string projectRoot, string relativePath, string source)
    {
        if (Encoding.UTF8.GetByteCount(source) > MaximumSourceBytes)
            throw new InvalidDataException("Execute C# source exceeds Foundry's 1 MiB safety limit.");
        var path = ResolveConfinedSourcePath(projectRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true));
        writer.Write(source);
    }

    public static bool WriteNewSourceOrVerify(
        string projectRoot,
        string relativePath,
        string source)
    {
        var path = ResolveConfinedSourcePath(projectRoot, relativePath);
        if (!File.Exists(path))
        {
            WriteNewSource(projectRoot, relativePath, source);
            return true;
        }

        var existing = File.ReadAllText(path, new UTF8Encoding(false, true));
        if (!string.Equals(existing, source, StringComparison.Ordinal))
            throw new IOException("The Execute C# destination already contains different source. Foundry will not overwrite it.");
        return false;
    }

    public static string Sha256(string source) => Convert.ToHexStringLower(
        SHA256.HashData(new UTF8Encoding(false, true).GetBytes(source)));

    private static string CreateSetArgumentSource(string variableName, string value) =>
        $$"""
        using System;

        // Generated by Creators Forge Foundry ({{SetArgumentRevision}}).
        // Edit freely: Foundry never regenerates or overwrites this file automatically.
        public class CPHInline
        {
            public bool Execute()
            {
                CPH.SetArgument({{CSharpLiteral(variableName)}}, {{CSharpLiteral(value)}});
                return true;
            }
        }
        """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static string CSharpLiteral(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (var character in value)
        {
            result.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(character) || character is '\u2028' or '\u2029' => $"\\u{(int)character:x4}",
                _ => character.ToString(),
            });
        }
        return result.Append('"').ToString();
    }

    private static string SafeSegment(string value)
    {
        var filtered = new string(value.Trim().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-').ToArray());
        filtered = filtered.Trim('.', '-');
        if (string.IsNullOrWhiteSpace(filtered) || filtered is "." or "..")
            throw new InvalidDataException("The entity ID cannot form a safe Execute C# source path.");
        return filtered;
    }
}
