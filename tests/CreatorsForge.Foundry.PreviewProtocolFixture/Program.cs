namespace CreatorsForge.Foundry.PreviewProtocolFixture;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2) return 2;
        await File.WriteAllTextAsync(Path.GetFullPath(args[1]), "{ malformed preview result");
        return 0;
    }
}
