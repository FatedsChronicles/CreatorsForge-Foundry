using CreatorsForge.Foundry.StreamerBot.ExportInspector;

if (args.Length != 3 || !string.Equals(args[0], "inspect", StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        "Usage: ExportInspector inspect <import-code-file> <output-directory>");
    return 2;
}

string inputPath = Path.GetFullPath(args[1]);
string outputDirectory = Path.GetFullPath(args[2]);

try
{
    if (new FileInfo(inputPath).Length > ExportEnvelope.MaxImportCodeCharacters)
    {
        throw new InvalidDataException(
            $"The import-code file exceeds the {ExportEnvelope.MaxImportCodeCharacters}-byte safety limit.");
    }

    string importCode = File.ReadAllText(inputPath);
    ExportInspectionReport report = ExportInspection.Inspect(
        Path.GetFileName(inputPath),
        importCode,
        outputDirectory);

    Console.WriteLine($"Inspected: {report.SourceName}");
    Console.WriteLine($"Envelope bytes: {report.EnvelopeBytes}");
    Console.WriteLine($"JSON bytes: {report.JsonBytes}");
    Console.WriteLine($"Distinct GUIDs: {report.DistinctGuidCount}");
    Console.WriteLine($"Absolute paths: {report.AbsolutePathProperties.Count}");
    Console.WriteLine($"Output: {outputDirectory}");
    return 0;
}
catch (Exception exception) when (
    exception is IOException
    or UnauthorizedAccessException
    or System.Text.Json.JsonException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
