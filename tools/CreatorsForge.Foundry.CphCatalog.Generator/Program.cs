using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Editor;

return await CatalogueGenerator.RunAsync(args);

internal static class CatalogueGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly HashSet<string> DeprecatedMethods = new(
        [
            "AddQuoteForTrovo",
            "GetUserVar",
            "SetUserVar",
            "UnsetUser",
            "UnsetUserVar",
        ],
        StringComparer.Ordinal);

    private static readonly Dictionary<string, Documentation> DocumentationByMethod =
        CreateDocumentation();

    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length is < 4 or > 6)
        {
            Console.Error.WriteLine(
                "Usage: CphCatalog.Generator OUTPUT STABLE_DIR ALPHA_DIR BETA1_DIR [BETA6_DIR] [STABLE107_DIR]");
            return 2;
        }

        var outputPath = Path.GetFullPath(arguments[0]);
        var inputs = new List<ProfileInput>
        {
            new("1.0.4-stable", "1.0.4", "stable", Path.GetFullPath(arguments[1])),
            new("1.0.5-alpha.34", "1.0.5-alpha.34", "alpha", Path.GetFullPath(arguments[2])),
            new("1.0.5-beta.1", "1.0.5-beta.1", "beta", Path.GetFullPath(arguments[3])),
        };
        if (arguments.Length >= 5)
        {
            inputs.Add(new(
                "1.0.5-beta.6",
                "1.0.5-beta.6",
                "beta",
                Path.GetFullPath(arguments[4])));
        }
        if (arguments.Length == 6)
        {
            inputs.Add(new(
                "1.0.7-stable",
                "1.0.7",
                "stable",
                Path.GetFullPath(arguments[5])));
        }

        var overloads = new Dictionary<string, OverloadBuilder>(StringComparer.Ordinal);
        var profiles = new List<CphCatalogueProfile>();
        foreach (var input in inputs)
        {
            var interfacePath = Path.Combine(
                input.Directory,
                "Streamer.bot.Plugin.Interface.dll");
            if (!File.Exists(interfacePath))
            {
                Console.Error.WriteLine($"Interface assembly not found: {interfacePath}");
                return 1;
            }

            profiles.Add(new(
                input.Id,
                input.ProductVersion,
                input.Channel,
                Convert.ToHexStringLower(
                    SHA256.HashData(await File.ReadAllBytesAsync(interfacePath)))));
            foreach (var method in ReadMethods(input.Directory))
            {
                var key = method.Identity;
                if (!overloads.TryGetValue(key, out var builder))
                {
                    builder = new(method);
                    overloads.Add(key, builder);
                }

                builder.Profiles.Add(input.Id);
            }
        }

        var methods = overloads.Values
            .GroupBy(value => value.Method.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateMethod(group.Key, group.ToArray()))
            .ToArray();
        var draft = new CphCatalogue(1, "1.0.0", profiles, methods);
        var draftJson = JsonSerializer.Serialize(draft, SerializerOptions);
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(draftJson)));
        var catalogue = draft with { Revision = $"1.0.0+{hash[..12]}" };
        var json = $"{JsonSerializer.Serialize(catalogue, SerializerOptions)}\n";

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine(
            $"Generated {methods.Length} methods and {overloads.Count} overloads ({catalogue.Revision}).");
        return 0;
    }

    private static MethodSnapshot[] ReadMethods(string hostDirectory)
    {
        var net481Directory = Path.Combine(
            AppContext.BaseDirectory,
            "ReferenceAssemblies",
            "net481");
        var paths = Directory
            .EnumerateFiles(net481Directory, "*.dll", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(hostDirectory, "*.dll"))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.FirstOrDefault(path => path.StartsWith(
                    hostDirectory,
                    StringComparison.OrdinalIgnoreCase)) ??
                group.First())
            .ToArray();
        var resolver = new PathAssemblyResolver(paths);
        using var context = new MetadataLoadContext(resolver, "mscorlib");
        var assembly = context.LoadFromAssemblyPath(
            Path.Combine(hostDirectory, "Streamer.bot.Plugin.Interface.dll"));
        var proxy = assembly.GetType(
            "Streamer.bot.Plugin.Interface.IInlineInvokeProxy",
            throwOnError: true)!;
        return proxy
            .GetMethods()
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(CreateIdentity, StringComparer.Ordinal)
            .Select(method => new MethodSnapshot(
                method.Name,
                CreateIdentity(method),
                FormatSignature(method),
                FormatType(method.ReturnType),
                method.GetParameters()
                    .Select(parameter => new ParameterSnapshot(
                        parameter.Name ?? "value",
                        FormatParameterType(parameter),
                        parameter.IsOptional,
                        parameter.IsOptional
                            ? FormatDefaultValue(parameter.RawDefaultValue)
                            : null))
                    .ToArray()))
            .ToArray();
    }

    private static CphMethod CreateMethod(
        string name,
        IReadOnlyList<OverloadBuilder> builders)
    {
        DocumentationByMethod.TryGetValue(name, out var docs);
        var allProfiles = builders
            .SelectMany(builder => builder.Profiles)
            .ToHashSet(StringComparer.Ordinal);
        var isStable = allProfiles.Contains("1.0.4-stable") ||
            allProfiles.Contains("1.0.7-stable");
        var status = DeprecatedMethods.Contains(name)
            ? "deprecated"
            : isStable
                ? "stable"
                : "prerelease";
        var overloads = builders
            .OrderBy(builder => builder.Method.Parameters.Length)
            .ThenBy(builder => builder.Method.Identity, StringComparer.Ordinal)
            .Select(builder => CreateOverload(builder, docs))
            .ToArray();

        return new(
            name,
            docs?.Category ?? InferCategory(name),
            docs?.Platform ?? InferPlatform(name),
            docs?.Summary ?? $"Streamer.bot CPH method {name}.",
            status,
            allProfiles.Contains("1.0.4-stable")
                ? "1.0.4"
                : allProfiles.Contains("1.0.7-stable")
                    ? "1.0.7"
                    : "1.0.5-alpha.34",
            docs?.Url,
            docs?.Example,
            docs?.Related ?? [],
            docs?.Cautions ?? [],
            overloads);
    }

    private static CphOverload CreateOverload(
        OverloadBuilder builder,
        Documentation? docs)
    {
        var method = builder.Method;
        var parameters = method.Parameters
            .Select(parameter => new CphParameter(
                parameter.Name,
                parameter.Type,
                docs?.ParameterDescriptions.GetValueOrDefault(parameter.Name) ??
                    $"Value for {parameter.Name}.",
                parameter.IsOptional,
                parameter.DefaultValue))
            .ToArray();
        return new(
            method.Signature,
            method.ReturnType,
            parameters,
            builder.Profiles.Order(StringComparer.Ordinal).ToArray());
    }

    private static string CreateIdentity(MethodInfo method) =>
        $"{method.Name}`{method.GetGenericArguments().Length}(" +
        $"{string.Join(",", method.GetParameters().Select(FormatParameterType))}):" +
        FormatType(method.ReturnType);

    private static string FormatSignature(MethodInfo method)
    {
        var generic = method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(type => type.Name))}>"
            : string.Empty;
        var parameters = string.Join(
            ", ",
            method.GetParameters().Select(parameter =>
            {
                var optional = parameter.IsOptional
                    ? $" = {FormatDefaultValue(parameter.RawDefaultValue)}"
                    : string.Empty;
                return $"{FormatParameterType(parameter)} {parameter.Name}{optional}";
            }));
        return $"{FormatType(method.ReturnType)} {method.Name}{generic}({parameters})";
    }

    private static string FormatParameterType(ParameterInfo parameter)
    {
        var prefix = parameter.IsOut
            ? "out "
            : parameter.ParameterType.IsByRef
                ? "ref "
                : string.Empty;
        var type = parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()!
            : parameter.ParameterType;
        return $"{prefix}{FormatType(type)}";
    }

    private static string FormatType(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsArray)
        {
            return $"{FormatType(type.GetElementType()!)}[]";
        }

        if (type.IsGenericType)
        {
            var name = (type.GetGenericTypeDefinition().FullName ?? type.Name)
                .Split('`')[0]
                .Replace('+', '.');
            return $"{Alias(name)}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
        }

        return Alias(type.FullName ?? type.Name);
    }

    private static string Alias(string name) => name switch
    {
        "System.Boolean" => "bool",
        "System.Byte" => "byte",
        "System.Char" => "char",
        "System.Decimal" => "decimal",
        "System.Double" => "double",
        "System.Int16" => "short",
        "System.Int32" => "int",
        "System.Int64" => "long",
        "System.Object" => "object",
        "System.Single" => "float",
        "System.String" => "string",
        "System.Void" => "void",
        _ => name,
    };

    private static string FormatDefaultValue(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
        char character => $"'{character}'",
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };

    private static string InferCategory(string name)
    {
        foreach (var prefix in new[]
                 {
                     "Command", "Global", "Group", "Midi", "Obs", "Quote",
                     "Sound", "Timer", "Twitch", "Websocket", "YouTube",
                 })
        {
            if (name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return prefix;
            }
        }

        return "General";
    }

    private static string InferPlatform(string name)
    {
        foreach (var platform in new[]
                 {
                     "Twitch", "YouTube", "Kick", "Trovo", "Obs", "VoiceMod",
                     "StreamDeck", "Meld",
                 })
        {
            if (name.StartsWith(platform, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(platform, StringComparison.OrdinalIgnoreCase))
            {
                return platform.ToLowerInvariant();
            }
        }

        return "streamerbot";
    }

    private static Dictionary<string, Documentation> CreateDocumentation() =>
        new Dictionary<string, Documentation>(StringComparer.Ordinal)
        {
            ["TryGetArg"] = new(
                "Arguments",
                "streamerbot",
                "Load an argument and convert it to the requested C# type.",
                "https://docs.streamer.bot/api/csharp/methods/core/arguments/try-get-arg",
                "if (!CPH.TryGetArg(\"userName\", out string userName)) return false;",
                new Dictionary<string, string>
                {
                    ["argName"] = "Name of the argument to read.",
                    ["value"] = "Receives the converted argument value.",
                }),
            ["SetArgument"] = new(
                "Arguments",
                "streamerbot",
                "Set an argument for subsequent sub-actions.",
                "https://docs.streamer.bot/api/csharp/methods/core/arguments/set-argument",
                "CPH.SetArgument(\"result\", value);",
                new Dictionary<string, string>
                {
                    ["variableName"] = "Name of the argument to set.",
                    ["value"] = "Value assigned to the argument.",
                }),
            ["SendMessage"] = new(
                "Chat",
                "twitch",
                "Send a Twitch chat message using the bot or broadcaster account.",
                "https://docs.streamer.bot/api/csharp/methods/twitch/chat/send-message",
                "CPH.SendMessage(\"Hello chat!\", true, true);",
                new Dictionary<string, string>
                {
                    ["message"] = "Chat message contents.",
                    ["useBot"] = "Use the connected bot account when true.",
                    ["fallback"] = "Fall back to the broadcaster account when needed.",
                }),
            ["RunAction"] = new(
                "Actions",
                "streamerbot",
                "Execute another Streamer.bot action by name.",
                "https://docs.streamer.bot/api/csharp/methods/core/actions/run-action",
                "CPH.RunAction(\"Update Overlay\", true);",
                new Dictionary<string, string>
                {
                    ["actionName"] = "Name of the action to execute.",
                    ["runImmediately"] = "Run inline when true; otherwise use its queue.",
                }),
            ["SetGlobalVar"] = new(
                "Globals",
                "streamerbot",
                "Set a persisted or non-persisted global variable.",
                "https://docs.streamer.bot/api/csharp/methods/core/globals/set-global-var",
                "CPH.SetGlobalVar(\"counter\", 1, true);",
                new Dictionary<string, string>
                {
                    ["varName"] = "Name of the global variable.",
                    ["value"] = "Serializable value to store.",
                    ["persisted"] = "Persist across restarts when true.",
                }),
            ["LogInfo"] = Simple(
                "Logging",
                "Write an informational message to the Streamer.bot log.",
                "CPH.LogInfo(\"Action started\");"),
            ["LogWarn"] = Simple(
                "Logging",
                "Write a warning message to the Streamer.bot log.",
                "CPH.LogWarn(\"Optional argument missing\");"),
            ["LogError"] = Simple(
                "Logging",
                "Write an error message to the Streamer.bot log.",
                "CPH.LogError(\"Action failed\");"),
            ["GetVersion"] = Simple(
                "System",
                "Return the running Streamer.bot version.",
                "string version = CPH.GetVersion();"),
            ["Wait"] = new(
                "System",
                "streamerbot",
                "Block execution for the specified duration.",
                null,
                "CPH.Wait(250);",
                new Dictionary<string, string>
                {
                    ["milliseconds"] = "Duration to block, in milliseconds.",
                },
                Cautions: ["Blocking waits can delay an action queue or UI workflow."]),
        };

    private static Documentation Simple(
        string category,
        string summary,
        string example) =>
        new(
            category,
            "streamerbot",
            summary,
            null,
            example,
            new Dictionary<string, string>
            {
                ["message"] = "Message written to the Streamer.bot log.",
            });

    private sealed record ProfileInput(
        string Id,
        string ProductVersion,
        string Channel,
        string Directory);

    private sealed class OverloadBuilder
    {
        public OverloadBuilder(MethodSnapshot method)
        {
            Method = method;
        }

        public MethodSnapshot Method { get; }

        public HashSet<string> Profiles { get; } = new(StringComparer.Ordinal);
    }

    private sealed record MethodSnapshot(
        string Name,
        string Identity,
        string Signature,
        string ReturnType,
        ParameterSnapshot[] Parameters);

    private sealed record ParameterSnapshot(
        string Name,
        string Type,
        bool IsOptional,
        string? DefaultValue);

    private sealed record Documentation(
        string Category,
        string Platform,
        string Summary,
        string? Url,
        string? Example,
        IReadOnlyDictionary<string, string> ParameterDescriptions,
        IReadOnlyList<string>? Related = null,
        IReadOnlyList<string>? Cautions = null);
}
