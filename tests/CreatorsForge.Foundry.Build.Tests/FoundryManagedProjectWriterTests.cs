using System.Xml.Linq;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class FoundryManagedProjectWriterTests
{
    [Fact]
    public async Task WinFormsFeatureAddsFrameworkDesktopReferences()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "FoundryManagedProjectWriterTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "build", "obj", "managed"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "Panel.cs"), "public sealed class Panel { }");
            var output = Path.Combine(root, "build", "obj", "managed", "Foundry.Managed.csproj");
            var manifest = new FoundryProjectManifest
            {
                Name = "Panel",
                Id = "com.example.panel",
                Version = "0.1.0",
                Features = new FoundryFeatures { WinForms = true },
                ManagedBuild = new FoundryManagedBuild
                {
                    AssemblyName = "Example.Panel",
                    Sources = ["src/Panel.cs"],
                },
                Outputs = [FoundryOutputKinds.ManagedLibrary],
            };

            await FoundryManagedProjectWriter.WriteAsync(
                manifest,
                root,
                output,
                CancellationToken.None);

            var document = XDocument.Load(output);
            var references = document.Descendants("Reference")
                .Select(item => item.Attribute("Include")?.Value)
                .ToArray();
            Assert.Contains("System.Drawing", references);
            Assert.Contains("System.Windows.Forms", references);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
