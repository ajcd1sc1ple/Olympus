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
    [InlineData(true, false, true, true, true)] // integration off — still block
    // Moving + Orbwalker active for job (WrathCombo CanOrbwalk): allow hardcast
    // without waiting for MovementLocked — Orbwalker locks/buffers the cast.
    [InlineData(true, true, true, false, false)]
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
