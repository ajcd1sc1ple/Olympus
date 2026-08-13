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
    public void HasMoved_IgnoresVerticalBob()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0f, 0.5f, 0f); // Y-only — animation / knockup recovery

        Assert.False(MovementGate.HasMoved(b, a, MovementGate.DefaultThresholdSquared));
    }

    [Fact]
    public void ThresholdSquaredFor_BossModIsMoreForgiving()
    {
        Assert.True(MovementGate.ThresholdSquaredFor(true) > MovementGate.ThresholdSquaredFor(false));
        Assert.True(MovementGate.SpeedThresholdFor(true) > MovementGate.SpeedThresholdFor(false));
    }

    [Theory]
    [InlineData(true, 1.0, 0.25f, true)]
    [InlineData(false, 0.1, 0.25f, true)]
    [InlineData(false, 0.3, 0.25f, false)]
    public void IsMoving_UsesGraceWindow(bool positionChanged, double secondsSince, float tolerance, bool expected)
    {
        Assert.Equal(expected, MovementGate.IsMoving(positionChanged, secondsSince, tolerance));
    }

    [Fact]
    public void IsMoving_Speed_IgnoresBossModArriveCrawl()
    {
        // BossMod stops pathing within ~0.1y; crawl under the BossMod speed floor must not block hardcasts.
        var crawlSpeed = 0.8f; // y/s — well below BossModSpeedThreshold (2.25)
        Assert.False(MovementGate.IsMoving(
            crawlSpeed,
            MovementGate.BossModSpeedThreshold,
            secondsSinceLastMovement: 1.0,
            movementToleranceSeconds: 0.25f));
    }

    [Fact]
    public void IsMoving_Speed_DetectsRealStrafe()
    {
        var strafeSpeed = 5.0f; // walk/run
        Assert.True(MovementGate.IsMoving(
            strafeSpeed,
            MovementGate.DefaultSpeedThreshold,
            secondsSinceLastMovement: 1.0,
            movementToleranceSeconds: 0.25f));
    }

    [Fact]
    public void HorizontalSpeed_UsesDeltaTime()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0.2f, 0f, 0f); // 0.2y in 1/60s ≈ 12 y/s
        var speed = MovementGate.HorizontalSpeed(b, a, 1f / 60f);
        Assert.InRange(speed, 11f, 13f);
    }

    [Fact]
    public void SmoothSpeed_BlendsTowardSample()
    {
        var smoothed = MovementGate.SmoothSpeed(0f, 10f, blend: 0.5f);
        Assert.Equal(5f, smoothed, 3);
        smoothed = MovementGate.SmoothSpeed(smoothed, 10f, blend: 0.5f);
        Assert.Equal(7.5f, smoothed, 3);
    }
}
