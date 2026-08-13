using Olympus.Services.Targeting;
using Xunit;

namespace Olympus.Tests.Services.Targeting;

public sealed class DamagePauseDecisionTests
{
    [Fact]
    public void ShouldPause_FalseWhenToggleOff()
    {
        Assert.False(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: false,
            hasHardTarget: false,
            playerInCombat: true,
            noTargetDurationMs: 5_000));
    }

    [Fact]
    public void ShouldPause_FalseWhenHardTargetPresent()
    {
        Assert.False(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: true,
            hasHardTarget: true,
            playerInCombat: true,
            noTargetDurationMs: 5_000));
    }

    [Fact]
    public void ShouldPause_FalseOutOfCombatEvenAfterGrace()
    {
        // Healers need Count/Find without a hard target for tank-pull bootstrap.
        Assert.False(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: true,
            hasHardTarget: false,
            playerInCombat: false,
            noTargetDurationMs: 5_000));
    }

    [Fact]
    public void ShouldPause_FalseDuringRetargetGraceWhileInCombat()
    {
        Assert.False(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: true,
            hasHardTarget: false,
            playerInCombat: true,
            noTargetDurationMs: DamagePauseDecision.NoTargetGraceMs - 1));
    }

    [Fact]
    public void ShouldPause_TrueAfterGraceWhileInCombat()
    {
        // Sustained drop = gaze / intentional disengage.
        Assert.True(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: true,
            hasHardTarget: false,
            playerInCombat: true,
            noTargetDurationMs: DamagePauseDecision.NoTargetGraceMs));
    }

    [Fact]
    public void ShouldPause_UnknownCombat_UsesGraceOnly()
    {
        Assert.False(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: true,
            hasHardTarget: false,
            playerInCombat: null,
            noTargetDurationMs: 0));

        Assert.True(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: true,
            hasHardTarget: false,
            playerInCombat: null,
            noTargetDurationMs: DamagePauseDecision.NoTargetGraceMs));
    }

    [Fact]
    public void AllowExplicitTargetFallback_NonStrictAlways()
    {
        Assert.True(DamagePauseDecision.AllowExplicitTargetFallback(
            strictCurrentTargetStrategy: false,
            hasHardTarget: false,
            noTargetDurationMs: 5_000));
    }

    [Fact]
    public void AllowExplicitTargetFallback_StrictAllowsDuringGrace()
    {
        Assert.True(DamagePauseDecision.AllowExplicitTargetFallback(
            strictCurrentTargetStrategy: true,
            hasHardTarget: false,
            noTargetDurationMs: 0));
    }

    [Fact]
    public void AllowExplicitTargetFallback_StrictBlocksAfterGrace()
    {
        Assert.False(DamagePauseDecision.AllowExplicitTargetFallback(
            strictCurrentTargetStrategy: true,
            hasHardTarget: false,
            noTargetDurationMs: DamagePauseDecision.NoTargetGraceMs));
    }
}
