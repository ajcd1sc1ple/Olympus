using System;
using Olympus.Rotation.Common.Helpers;
using Xunit;

namespace Olympus.Tests.Rotation.Common.Helpers;

public class PostCancelCastHoldTests
{
    [Fact]
    public void UpdateHoldUntil_FallingEdgeWhileMoving_ArmsHold()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hold = TimeSpan.FromSeconds(0.4);

        var until = PostCancelCastHold.UpdateHoldUntil(
            wasCastingCastTimeGcd: true,
            isCasting: false,
            isMoving: true,
            now: now,
            holdDuration: hold,
            currentHoldUntil: DateTime.MinValue);

        Assert.Equal(now + hold, until);
        Assert.True(PostCancelCastHold.ShouldBlock(now.AddSeconds(0.2), until));
        Assert.False(PostCancelCastHold.ShouldBlock(now.AddSeconds(0.5), until));
    }

    [Fact]
    public void UpdateHoldUntil_FallingEdgeWhileStationary_DoesNotArm()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var until = PostCancelCastHold.UpdateHoldUntil(
            wasCastingCastTimeGcd: true,
            isCasting: false,
            isMoving: false,
            now: now,
            holdDuration: TimeSpan.FromSeconds(0.4),
            currentHoldUntil: DateTime.MinValue);

        Assert.Equal(DateTime.MinValue, until);
        Assert.False(PostCancelCastHold.ShouldBlock(now, until));
    }

    [Fact]
    public void UpdateHoldUntil_StillCasting_PreservesExistingHold()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = now.AddSeconds(1);

        var until = PostCancelCastHold.UpdateHoldUntil(
            wasCastingCastTimeGcd: true,
            isCasting: true,
            isMoving: true,
            now: now,
            holdDuration: TimeSpan.FromSeconds(0.4),
            currentHoldUntil: existing);

        Assert.Equal(existing, until);
    }

    [Fact]
    public void UpdateHoldUntil_Stopped_ClearsExistingHold()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = now.AddSeconds(1);

        var until = PostCancelCastHold.UpdateHoldUntil(
            wasCastingCastTimeGcd: false,
            isCasting: false,
            isMoving: false,
            now: now,
            holdDuration: TimeSpan.FromSeconds(0.4),
            currentHoldUntil: existing);

        Assert.Equal(DateTime.MinValue, until);
        Assert.False(PostCancelCastHold.ShouldBlock(now, until));
    }

    [Theory]
    [InlineData(0.1f, 0.3f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(0.9f, 0.6f)]
    public void ClampHoldSeconds_ClampsToWindow(float input, float expected)
    {
        Assert.Equal(expected, PostCancelCastHold.ClampHoldSeconds(input), 3);
    }
}
