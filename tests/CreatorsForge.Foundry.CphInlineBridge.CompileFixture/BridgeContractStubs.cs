using System;
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

namespace CreatorsForge.Tests.Extension
{
    public static class EntryPoint
    {
        public static bool Execute(
            IDictionary<string, object> arguments,
            Action<string> logInformation)
        {
            logInformation("compile-fixture");
            return arguments != null;
        }
    }
}
