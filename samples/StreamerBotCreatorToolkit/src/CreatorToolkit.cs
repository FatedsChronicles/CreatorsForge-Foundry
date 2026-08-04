using System;
using System.Collections.Generic;
using System.Globalization;

namespace CreatorsForge.Samples.CreatorToolkit
{
    public static class CreatorToolkit
    {
        public static bool Execute(IDictionary<string, object> arguments, Action<string> logInformation)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            if (logInformation == null) throw new ArgumentNullException(nameof(logInformation));

            string workflow = Read(arguments, "workflow", "welcome").ToLowerInvariant();
            string user = Read(arguments, "user", "creator");
            string message;
            switch (workflow)
            {
                case "shoutout":
                    message = "[Creator Toolkit] Shout-out queued for " + user + ".";
                    break;
                case "milestone":
                    message = "[Creator Toolkit] Milestone celebrated with " + user + ".";
                    break;
                default:
                    message = "[Creator Toolkit] Welcome, " + user + "!";
                    break;
            }

            int sequence;
            object suppliedSequence;
            if (arguments.TryGetValue("sequence", out suppliedSequence) &&
                int.TryParse(Convert.ToString(suppliedSequence, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) && sequence > 0)
            {
                message += " Event #" + sequence.ToString(CultureInfo.InvariantCulture) + ".";
            }
            logInformation(message);
            return true;
        }

        private static string Read(IDictionary<string, object> arguments, string key, string fallback)
        {
            object value;
            if (!arguments.TryGetValue(key, out value)) return fallback;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        }
    }
}

