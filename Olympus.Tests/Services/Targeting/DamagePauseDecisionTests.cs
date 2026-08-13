using Olympus.Services.Targeting;
using Xunit;

namespace Olympus.Tests.Services.Targeting;

public sealed class DamagePauseDecisionTests
{
    [Fact]
    public void ShouldPause_NeverStallsDamage()
    {
        // PauseWhenNoTarget stalls froze dual-boss / Tab / dive DPS — removed.
        Assert.False(DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: true,
            hasHardTarget: false,
            playerInCombat: true,
            noTargetDurationMs: 5_000));

        Assert.False(DamagePauseDecision.ShouldPause(
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
    public void AllowExplicitTargetFallback_StrictHonorsUsableHardTarget()
    {
        Assert.False(DamagePauseDecision.AllowExplicitTargetFallback(
            strictCurrentTargetStrategy: true,
            hasHardTarget: true,
            noTargetDurationMs: 0,
            hardTargetUsable: true));
    }

    [Fact]
    public void AllowExplicitTargetFallback_UntargetableHardTargetFallsBack()
    {
        // Anyder diving shark — keep DPS on the other selectable boss.
        Assert.True(DamagePauseDecision.AllowExplicitTargetFallback(
            strictCurrentTargetStrategy: true,
            hasHardTarget: true,
            noTargetDurationMs: 0,
            hardTargetUsable: false));
    }

    [Fact]
    public void AllowExplicitTargetFallback_NullHardTargetFallsBack()
    {
        Assert.True(DamagePauseDecision.AllowExplicitTargetFallback(
            strictCurrentTargetStrategy: true,
            hasHardTarget: false,
            noTargetDurationMs: DamagePauseDecision.NoTargetGraceMs));
    }
}
