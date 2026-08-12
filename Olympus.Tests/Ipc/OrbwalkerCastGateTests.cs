using Olympus.Ipc;
using Xunit;

namespace Olympus.Tests.Ipc;

public class OrbwalkerCastGateTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void ShouldBlockHardcasts_MatchesExpected(
        bool isMoving,
        bool integrationEnabled,
        bool orbwalkerActiveForJob,
        bool expectedBlock)
    {
        var result = OrbwalkerCastGate.ShouldBlockHardcasts(
            isMoving,
            integrationEnabled,
            orbwalkerActiveForJob);

        Assert.Equal(expectedBlock, result);
    }
}
