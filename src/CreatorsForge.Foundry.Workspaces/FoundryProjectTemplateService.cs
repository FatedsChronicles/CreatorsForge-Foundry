using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryProjectTemplateDescriptor(
    string Id,
    string Name,
    string Provider,
    string Kind,
    string Description,
    string Revision);

public static class FoundryProjectTemplateService
{
    public const string StreamerBotExtension = "streamerbot-extension-v1";
    public const string StreamerBotCommand = "streamerbot-command-v1";
    public const string ObsModule = "obs-module-v1";
    public const string ObsPassthroughFilter = "obs-passthrough-filter-v1";
    public const string ObsConfigurableFilter = "obs-configurable-filter-v1";
    public const string ObsVideoInput = "obs-video-input-v1";
    public const string ObsOutput = "obs-output-v1";

    public static IReadOnlyList<FoundryProjectTemplateDescriptor> Templates { get; } =
    [
        new(StreamerBotExtension, "Streamer.bot extension", "streamerbot", "Extension", "Minimal test-triggered extension and CPHInline bridge.", "1.0.1"),
        new(StreamerBotCommand, "Streamer.bot command workflow", "streamerbot", "Workflow", "Command, queue, action, and managed bridge starter.", "1.0.1"),
        new(ObsModule, "OBS module", "obsstudio", "Module", "SDK-backed module without a registered component.", "1.0.1"),
        new(ObsPassthroughFilter, "OBS passthrough filter", "obsstudio", "Filter", "Lifecycle-safe synchronous video filter.", "1.0.1"),
        new(ObsConfigurableFilter, "OBS configurable filter", "obsstudio", "Filter", "Video filter with defaults and a property form.", "1.0.1"),
        new(ObsVideoInput, "OBS video input", "obsstudio", "Source", "Lifecycle-safe 1920x1080 input source skeleton.", "1.0.1"),
        new(ObsOutput, "OBS encoded output", "obsstudio", "Output", "Lifecycle-safe encoded output skeleton with start/stop callbacks.", "1.0.1"),
    ];

    public static FoundryProjectTemplateDescriptor? Find(string provider, string? templateId)
    {
        var fallback = string.Equals(provider, "obsstudio", StringComparison.Ordinal)
            ? ObsPassthroughFilter
            : StreamerBotCommand;
        var id = string.IsNullOrWhiteSpace(templateId) ? fallback : templateId;
        return Templates.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.Ordinal) &&
            string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    public static string? GetObsDesignTemplate(string projectTemplateId) => projectTemplateId switch
    {
        ObsModule => FoundryObsDesign.ModuleStarterTemplate,
        ObsPassthroughFilter => FoundryObsDesign.PassthroughFilterTemplate,
        ObsConfigurableFilter => FoundryObsDesign.ConfigurableFilterTemplate,
        ObsVideoInput => FoundryObsDesign.VideoInputTemplate,
        ObsOutput => FoundryObsDesign.OutputTemplate,
        _ => null,
    };
}
