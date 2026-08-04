using System.Globalization;

namespace CreatorsForge.Samples.HelloFoundry;

public static class HelloExtension
{
    public static bool Execute(
        IDictionary<string, object> arguments,
        Action<string> logInformation)
    {
        arguments.TryGetValue("message", out var suppliedMessage);
        var message = Convert.ToString(suppliedMessage, CultureInfo.InvariantCulture);
        logInformation(string.IsNullOrWhiteSpace(message)
            ? "Hello from Creators Forge Foundry."
            : message);
        return true;
    }
}
