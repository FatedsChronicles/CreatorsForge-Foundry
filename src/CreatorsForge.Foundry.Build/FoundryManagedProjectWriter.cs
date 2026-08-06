using System.Text;
using System.Xml;
using System.Xml.Linq;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build;

public static class FoundryManagedProjectWriter
{
    private const string ReferenceAssembliesVersion = "1.0.3";

    public static async Task WriteAsync(
        FoundryProjectManifest manifest,
        string projectRoot,
        string generatedProjectPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedProjectPath);

        var build = manifest.ManagedBuild ??
            throw new ArgumentException("Managed build settings are required.", nameof(manifest));
        var generatedProjectDirectory =
            Path.GetDirectoryName(generatedProjectPath) ??
            throw new ArgumentException(
                "The generated project path has no directory.",
                nameof(generatedProjectPath));

        var project = new XElement(
            "Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement(
                "PropertyGroup",
                new XElement("TargetFramework", build.TargetFramework),
                new XElement("LangVersion", build.LanguageVersion),
                new XElement("AssemblyName", build.AssemblyName),
                new XElement("RootNamespace", build.AssemblyName),
                new XElement("Version", manifest.Version),
                new XElement("EnableDefaultCompileItems", "false"),
                new XElement("ImplicitUsings", "disable"),
                new XElement("Nullable", "disable"),
                new XElement("Deterministic", "true"),
                new XElement("ContinuousIntegrationBuild", "true"),
                new XElement("DebugType", "embedded"),
                new XElement("DebugSymbols", "true"),
                new XElement("Optimize", "true"),
                new XElement("TreatWarningsAsErrors", "true"),
                new XElement(
                    "PathMap",
                    @"$(MSBuildProjectDirectory)\..\..\..=/_/")),
            new XElement(
                "ItemGroup",
                new XElement(
                    "PackageReference",
                    new XAttribute("Include", "Microsoft.NETFramework.ReferenceAssemblies"),
                    new XAttribute("Version", ReferenceAssembliesVersion),
                    new XAttribute("PrivateAssets", "all"))),
            manifest.Features.WinForms
                ? new XElement(
                    "ItemGroup",
                    new XElement(
                        "Reference",
                        new XAttribute("Include", "System.Drawing")),
                    new XElement(
                        "Reference",
                        new XAttribute("Include", "System.Windows.Forms")))
                : null,
            new XElement(
                "ItemGroup",
                build.Sources.Select(source =>
                {
                    var fullSourcePath = Path.GetFullPath(
                        Path.Combine(
                            projectRoot,
                            source.Replace('/', Path.DirectorySeparatorChar)));
                    var relativePath = Path.GetRelativePath(
                        generatedProjectDirectory,
                        fullSourcePath).Replace('\\', '/');
                    return new XElement(
                        "Compile",
                        new XAttribute("Include", EscapeMsBuild(relativePath)));
                })));

        var document = new XDocument(project);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
        };

        var xml = new StringBuilder();
        using (var writer = XmlWriter.Create(xml, settings))
        {
            document.Save(writer);
        }

        await File.WriteAllTextAsync(
            generatedProjectPath,
            $"{xml}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeMsBuild(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            escaped.Append(character switch
            {
                '%' => "%25",
                '*' => "%2A",
                '?' => "%3F",
                '@' => "%40",
                '$' => "%24",
                '(' => "%28",
                ')' => "%29",
                ';' => "%3B",
                '\'' => "%27",
                _ => character,
            });
        }

        return escaped.ToString();
    }
}
