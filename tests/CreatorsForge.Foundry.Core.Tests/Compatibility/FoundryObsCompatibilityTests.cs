using CreatorsForge.Foundry.Core.Compatibility;

namespace CreatorsForge.Foundry.Core.Tests.Compatibility;

public sealed class FoundryObsCompatibilityTests
{
    [Theory]
    [InlineData("32.1.2")]
    [InlineData("32.2.1")]
    [InlineData("32.2.1+release")]
    public void ExactVerifiedRuntimesAreSupported(string version)
    {
        Assert.True(FoundryObsCompatibility.IsSupportedRuntime(version));
    }

    [Theory]
    [InlineData("32.1.1")]
    [InlineData("32.2.0")]
    [InlineData("33.0.0")]
    public void UnverifiedRuntimesRemainUnsupported(string version)
    {
        Assert.False(FoundryObsCompatibility.IsSupportedRuntime(version));
    }
}
