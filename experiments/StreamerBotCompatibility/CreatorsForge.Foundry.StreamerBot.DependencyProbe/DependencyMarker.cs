namespace CreatorsForge.Foundry.StreamerBot.DependencyProbe
{
    public static class DependencyMarker
    {
        public const string ExpectedValue = "foundry-dependency-loaded";

        public static string GetValue()
        {
            return ExpectedValue;
        }
    }
}
