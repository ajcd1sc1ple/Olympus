using System.Numerics;
using Olympus.Rotation.Common.Helpers;
using Xunit;

namespace Olympus.Tests.Rotation.Common.Helpers;

public class MovementGateTests
{
    [Fact]
    public void HasMoved_IgnoresSubThresholdJitter()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0.05f, 0f, 0f); // 0.05y < 0.15y default

        Assert.False(MovementGate.HasMoved(b, a, MovementGate.DefaultThresholdSquared));
    }

    [Fact]
    public void HasMoved_DetectsRealStep()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0.2f, 0f, 0f);

        Assert.True(MovementGate.HasMoved(b, a, MovementGate.DefaultThresholdSquared));
    }

    [Fact]
    public void ThresholdSquaredFor_BossModIsMoreForgiving()
    {
        Assert.True(MovementGate.ThresholdSquaredFor(true) > MovementGate.ThresholdSquaredFor(false));
    }

    [Theory]
    [InlineData(true, 1.0, 0.25f, true)]
    [InlineData(false, 0.1, 0.25f, true)]
    [InlineData(false, 0.3, 0.25f, false)]
    public void IsMoving_UsesGraceWindow(bool positionChanged, double secondsSince, float tolerance, bool expected)
    {
        Assert.Equal(expected, MovementGate.IsMoving(positionChanged, secondsSince, tolerance));
    }
}
