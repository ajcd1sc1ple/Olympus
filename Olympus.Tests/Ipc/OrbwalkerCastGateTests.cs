using Olympus.Ipc;
using Xunit;

namespace Olympus.Tests.Ipc;

public class OrbwalkerCastGateTests
{
    [Theory]
    // Stationary: never block
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, true, true, false)]
    // Moving, no Orbwalker coverage: block
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, true, false, true)] // active but not locked yet — still block
    [InlineData(true, false, true, true, true)] // integration off — still block
    // Moving + Orbwalker actively locking: allow hardcast
    [InlineData(true, true, true, true, false)]
    public void ShouldBlockHardcasts_MatchesExpected(
        bool isMoving,
        bool integrationEnabled,
        bool orbwalkerActiveForJob,
        bool orbwalkerMovementLocked,
        bool expectedBlock)
    {
        var result = OrbwalkerCastGate.ShouldBlockHardcasts(
            isMoving,
            integrationEnabled,
            orbwalkerActiveForJob,
            orbwalkerMovementLocked);

        Assert.Equal(expectedBlock, result);
    }
}
