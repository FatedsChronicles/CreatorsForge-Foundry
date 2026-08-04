using System;
using CreatorsForge.Foundry.StreamerBot.CompatibilityProbe;
using Streamer.bot.Plugin.Interface;

#if EXTERNAL_EDITOR
public sealed class FoundryCompatibilityBridge : CPHInlineBase
#else
public class CPHInline
#endif
{
#if EXTERNAL_EDITOR
    public override bool Execute()
#else
    public bool Execute()
#endif
    {
        ProbeResult result = CompatibilityProbe.Run(
            args,
            message => CPH.LogInfo(message),
            () => CPH.GetVersion());

        CPH.SetArgument("foundryProbeSuccess", result.Success);
        CPH.SetArgument("foundryProbeInputObserved", result.Input);
        CPH.SetArgument("foundryProbeHostVersion", result.HostVersion);
        CPH.SetArgument("foundryProbeDependency", result.DependencyValue);
        CPH.SetArgument("foundryProbeMessage", result.Message);

        return result.Success;
    }
}
