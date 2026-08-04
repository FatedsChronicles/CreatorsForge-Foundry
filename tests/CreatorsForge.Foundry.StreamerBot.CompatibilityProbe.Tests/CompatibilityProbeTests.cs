using System.Collections.Generic;
using CreatorsForge.Foundry.StreamerBot.CompatibilityProbe;
using CreatorsForge.Foundry.StreamerBot.DependencyProbe;
using Xunit;

namespace CreatorsForge.Foundry.StreamerBot.CompatibilityProbe.Tests
{
    public sealed class CompatibilityProbeTests
    {
        [Fact]
        public void RunCapturesArgumentsHostCallsAndDependencyOutput()
        {
            var arguments = new Dictionary<string, object>
            {
                [CompatibilityProbe.InputArgumentName] = CompatibilityProbe.ExpectedInput,
            };
            string loggedMessage = null;

            ProbeResult result = CompatibilityProbe.Run(
                arguments,
                message => loggedMessage = message,
                () => "1.0.4");

            Assert.True(result.Success);
            Assert.Equal(CompatibilityProbe.ExpectedInput, result.Input);
            Assert.Equal("1.0.4", result.HostVersion);
            Assert.Equal(DependencyMarker.ExpectedValue, result.DependencyValue);
            Assert.Same(result.Message, loggedMessage);
        }

        [Fact]
        public void RunReturnsFailureAndStillLogsWhenRequiredInputIsMissing()
        {
            var arguments = new Dictionary<string, object>();
            string loggedMessage = null;

            ProbeResult result = CompatibilityProbe.Run(
                arguments,
                message => loggedMessage = message,
                () => "1.0.5-beta.1");

            Assert.False(result.Success);
            Assert.Empty(result.Input);
            Assert.NotNull(loggedMessage);
            Assert.Contains("success=False", loggedMessage);
        }
    }
}
