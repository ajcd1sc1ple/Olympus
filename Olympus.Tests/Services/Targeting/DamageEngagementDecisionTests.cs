using Olympus.Services.Targeting;
using Xunit;

namespace Olympus.Tests.Services.Targeting;

public sealed class DamageEngagementDecisionTests
{
    [Theory]
    [InlineData(true, false, false, false, true)]  // hard target
    [InlineData(false, true, false, false, true)] // player in combat
    [InlineData(false, false, true, false, true)] // enemy in combat
    [InlineData(false, false, false, true, true)] // sole hostile bootstrap
    [InlineData(false, false, false, false, false)] // OOC multi-pack: blocked
    public void IsSelectable_MatchesExpected(
        bool isHardTarget,
        bool playerInCombat,
        bool enemyInCombat,
        bool isSoleHostile,
        bool expected)
    {
        Assert.Equal(expected, DamageEngagementDecision.IsSelectable(
            isHardTarget, playerInCombat, enemyInCombat, isSoleHostile));
    }
}
