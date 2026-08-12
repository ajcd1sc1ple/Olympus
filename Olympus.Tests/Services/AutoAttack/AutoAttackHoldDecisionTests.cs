using Olympus.Services.AutoAttack;
using Xunit;

namespace Olympus.Tests.Services.AutoAttack;

public class AutoAttackHoldDecisionTests
{
    [Fact]
    public void GetDesiredState_LivingTargetWhileHolding_EnablesAutoAttack()
    {
        var desired = AutoAttackHoldDecision.GetDesiredState(
            managementEnabled: true,
            pluginEnabled: true,
            playerAlive: true,
            inCombat: false,
            currentlyAutoAttacking: false,
            hasLivingHostileTarget: true,
            targetIsDead: false,
            engagedTargetStillAlive: true,
            engagedTargetDied: false,
            isHoldingUntilDead: true,
            standStillPunisherActive: false);

        Assert.True(desired);
    }

    [Fact]
    public void GetDesiredState_TargetDied_DisablesAutoAttack()
    {
        var desired = AutoAttackHoldDecision.GetDesiredState(
            managementEnabled: true,
            pluginEnabled: true,
            playerAlive: true,
            inCombat: true,
            currentlyAutoAttacking: true,
            hasLivingHostileTarget: false,
            targetIsDead: true,
            engagedTargetStillAlive: false,
            engagedTargetDied: true,
            isHoldingUntilDead: true,
            standStillPunisherActive: false);

        Assert.False(desired);
    }

    [Fact]
    public void GetDesiredState_DoesNotEnablePrePullWithoutCombatOrHold()
    {
        var desired = AutoAttackHoldDecision.GetDesiredState(
            managementEnabled: true,
            pluginEnabled: true,
            playerAlive: true,
            inCombat: false,
            currentlyAutoAttacking: false,
            hasLivingHostileTarget: true,
            targetIsDead: false,
            engagedTargetStillAlive: false,
            engagedTargetDied: false,
            isHoldingUntilDead: false,
            standStillPunisherActive: false);

        Assert.Null(desired);
    }

    [Fact]
    public void GetDesiredState_PyreticForcesOff()
    {
        var desired = AutoAttackHoldDecision.GetDesiredState(
            managementEnabled: true,
            pluginEnabled: true,
            playerAlive: true,
            inCombat: true,
            currentlyAutoAttacking: true,
            hasLivingHostileTarget: true,
            targetIsDead: false,
            engagedTargetStillAlive: true,
            engagedTargetDied: false,
            isHoldingUntilDead: true,
            standStillPunisherActive: true);

        Assert.False(desired);
    }

    [Fact]
    public void GetDesiredState_GazeDropWhileHolding_DoesNotForceChange()
    {
        var desired = AutoAttackHoldDecision.GetDesiredState(
            managementEnabled: true,
            pluginEnabled: true,
            playerAlive: true,
            inCombat: true,
            currentlyAutoAttacking: false,
            hasLivingHostileTarget: false,
            targetIsDead: false,
            engagedTargetStillAlive: true,
            engagedTargetDied: false,
            isHoldingUntilDead: true,
            standStillPunisherActive: false);

        Assert.Null(desired);
    }

    [Fact]
    public void ShouldHold_StartsOnCombatWithLivingTarget()
    {
        Assert.True(AutoAttackHoldDecision.ShouldHold(
            currentlyHolding: false,
            inCombat: true,
            currentlyAutoAttacking: false,
            hasLivingHostileTarget: true,
            engagedTargetStillAlive: true,
            targetIsDead: false,
            engagedTargetDied: false,
            standStillPunisherActive: false,
            pluginEnabled: true,
            playerAlive: true));
    }

    [Fact]
    public void ShouldHold_StartsWhenAutoAttackingLivingTarget()
    {
        Assert.True(AutoAttackHoldDecision.ShouldHold(
            currentlyHolding: false,
            inCombat: false,
            currentlyAutoAttacking: true,
            hasLivingHostileTarget: true,
            engagedTargetStillAlive: true,
            targetIsDead: false,
            engagedTargetDied: false,
            standStillPunisherActive: false,
            pluginEnabled: true,
            playerAlive: true));
    }

    [Fact]
    public void ShouldHold_DoesNotStartOnTargetAloneWithoutAaOrCombat()
    {
        Assert.False(AutoAttackHoldDecision.ShouldHold(
            currentlyHolding: false,
            inCombat: false,
            currentlyAutoAttacking: false,
            hasLivingHostileTarget: true,
            engagedTargetStillAlive: true,
            targetIsDead: false,
            engagedTargetDied: false,
            standStillPunisherActive: false,
            pluginEnabled: true,
            playerAlive: true));
    }

    [Fact]
    public void ShouldHold_KeepsHoldWhenInCombatFlickersOff()
    {
        Assert.True(AutoAttackHoldDecision.ShouldHold(
            currentlyHolding: true,
            inCombat: false,
            currentlyAutoAttacking: false,
            hasLivingHostileTarget: true,
            engagedTargetStillAlive: true,
            targetIsDead: false,
            engagedTargetDied: false,
            standStillPunisherActive: false,
            pluginEnabled: true,
            playerAlive: true));
    }

    [Fact]
    public void ShouldHold_ReleasesOnDeath()
    {
        Assert.False(AutoAttackHoldDecision.ShouldHold(
            currentlyHolding: true,
            inCombat: true,
            currentlyAutoAttacking: true,
            hasLivingHostileTarget: false,
            engagedTargetStillAlive: false,
            targetIsDead: true,
            engagedTargetDied: true,
            standStillPunisherActive: false,
            pluginEnabled: true,
            playerAlive: true));
    }

    [Fact]
    public void ShouldTreatAsInCombat_WhileHoldingOrAutoAttackingLivingHardTarget()
    {
        Assert.True(AutoAttackHoldDecision.ShouldTreatAsInCombat(true, true, true));
        Assert.True(AutoAttackHoldDecision.ShouldTreatAsInCombat(true, false, true, currentlyAutoAttacking: true));
        Assert.False(AutoAttackHoldDecision.ShouldTreatAsInCombat(true, false, true, currentlyAutoAttacking: false));
        Assert.False(AutoAttackHoldDecision.ShouldTreatAsInCombat(true, true, false));
        Assert.False(AutoAttackHoldDecision.ShouldTreatAsInCombat(false, true, true));
    }
}
