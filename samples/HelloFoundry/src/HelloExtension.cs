using System;
using System.Collections.Generic;
using System.Globalization;

namespace CreatorsForge.Samples.HelloFoundry
{
    public static class HelloExtension
    {
        public static bool Execute(
            IDictionary<string, object> arguments,
            Action<string> logInformation)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            if (logInformation == null)
            {
                throw new ArgumentNullException(nameof(logInformation));
            }

            object suppliedMessage;
            arguments.TryGetValue("message", out suppliedMessage);
            string message = Convert.ToString(
                suppliedMessage,
                CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "Hello from Creators Forge Foundry.";
            }

            logInformation(message);
            return true;
        }
    }
}
