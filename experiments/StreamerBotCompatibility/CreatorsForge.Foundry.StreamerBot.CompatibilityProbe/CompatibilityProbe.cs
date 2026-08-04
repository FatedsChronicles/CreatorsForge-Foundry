using System;
using System.Collections.Generic;
using System.Globalization;
using CreatorsForge.Foundry.StreamerBot.DependencyProbe;

namespace CreatorsForge.Foundry.StreamerBot.CompatibilityProbe
{
    public static class CompatibilityProbe
    {
        public const string InputArgumentName = "foundryProbeInput";
        public const string ExpectedInput = "foundry-probe";

        public static ProbeResult Run(
            IDictionary<string, object> arguments,
            Action<string> logInformation,
            Func<string> getHostVersion)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            if (logInformation == null)
            {
                throw new ArgumentNullException(nameof(logInformation));
            }

            if (getHostVersion == null)
            {
                throw new ArgumentNullException(nameof(getHostVersion));
            }

            object inputValue;
            arguments.TryGetValue(InputArgumentName, out inputValue);

            string input = Convert.ToString(inputValue, CultureInfo.InvariantCulture) ?? string.Empty;
            string hostVersion = getHostVersion() ?? string.Empty;
            string dependencyValue = DependencyMarker.GetValue();
            bool success = string.Equals(input, ExpectedInput, StringComparison.Ordinal)
                && string.Equals(
                    dependencyValue,
                    DependencyMarker.ExpectedValue,
                    StringComparison.Ordinal);

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "Creators Forge Foundry probe: success={0}; host={1}; input={2}; dependency={3}",
                success,
                hostVersion,
                input,
                dependencyValue);

            logInformation(message);

            return new ProbeResult(success, input, hostVersion, dependencyValue, message);
        }
    }

    public sealed class ProbeResult
    {
        public ProbeResult(
            bool success,
            string input,
            string hostVersion,
            string dependencyValue,
            string message)
        {
            Success = success;
            Input = input;
            HostVersion = hostVersion;
            DependencyValue = dependencyValue;
            Message = message;
        }

        public bool Success { get; }

        public string Input { get; }

        public string HostVersion { get; }

        public string DependencyValue { get; }

        public string Message { get; }
    }
}
