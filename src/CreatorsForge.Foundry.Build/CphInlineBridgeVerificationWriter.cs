using System.Text;
using System.Xml;
using System.Xml.Linq;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build;

public static class CphInlineBridgeVerificationWriter
{
    private const string ReferenceAssembliesVersion = "1.0.3";

    private const string StubSource = """
        using System.Collections.Generic;

        namespace Streamer.bot.Plugin.Interface
        {
            public abstract class CPHInlineBase
            {
                protected readonly IDictionary<string, object> args =
                    new Dictionary<string, object>();

                protected readonly StubCph CPH = new StubCph();

                public abstract bool Execute();
            }

            public sealed class StubCph
            {
                public void LogInfo(string message)
                {
                }
            }
        }
        """;

    public static async Task WriteAsync(
        FoundryManagedBuild managedBuild,
        string verificationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(managedBuild);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationDirectory);

        var project = new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement(
                    "PropertyGroup",
                    new XElement("TargetFramework", managedBuild.TargetFramework),
                    new XElement("LangVersion", managedBuild.LanguageVersion),
                    new XElement("DefineConstants", "EXTERNAL_EDITOR"),
                    new XElement("EnableDefaultCompileItems", "false"),
                    new XElement("EnableNETAnalyzers", "false"),
                    new XElement("ImplicitUsings", "disable"),
                    new XElement("Nullable", "disable"),
                    new XElement("Deterministic", "true"),
                    new XElement("TreatWarningsAsErrors", "true")),
                new XElement(
                    "ItemGroup",
                    new XElement(
                        "PackageReference",
                        new XAttribute("Include", "Microsoft.NETFramework.ReferenceAssemblies"),
                        new XAttribute("Version", ReferenceAssembliesVersion),
                        new XAttribute("PrivateAssets", "all"))),
                new XElement(
                    "ItemGroup",
                    new XElement(
                        "Reference",
                        new XAttribute("Include", managedBuild.AssemblyName),
                        new XElement(
                            "HintPath",
                            $"../../managed/{managedBuild.AssemblyName}.dll"),
                        new XElement("Private", "false"))),
                new XElement(
                    "ItemGroup",
                    new XElement(
                        "Compile",
                        new XAttribute("Include", "BridgeContractStubs.cs")),
                    new XElement(
                        "Compile",
                        new XAttribute("Include", "../../bridge/CPHInline.cs"),
                        new XAttribute("Link", "CPHInline.cs")))));

        await WriteXmlAsync(
            project,
            Path.Combine(verificationDirectory, "Foundry.BridgeVerify.csproj"),
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(verificationDirectory, "BridgeContractStubs.cs"),
            $"{StubSource}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteXmlAsync(
        XDocument document,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var settings = new XmlWriterSettings
        {
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
            destinationPath,
            $"{xml}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
    }
}
