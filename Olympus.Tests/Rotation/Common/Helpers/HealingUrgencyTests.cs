using Olympus.Config;
using Olympus.Rotation.Common.Helpers;
using Xunit;

namespace Olympus.Tests.Rotation.Common.Helpers;

public class HealingUrgencyTests
{
    [Theory]
    [InlineData(true, 0.40f, 0.40f, true)]
    [InlineData(true, 0.39f, 0.40f, true)]
    [InlineData(true, 0.41f, 0.40f, false)]
    [InlineData(false, 0.10f, 0.40f, false)]
    public void ShouldSuppressDamageGcds_MatchesThresholdAndHealToggle(
        bool healingEnabled, float lowestHp, float threshold, bool expected)
    {
        var healing = new HealingConfig { GcdEmergencyThreshold = threshold };
        Assert.Equal(expected, HealingUrgency.ShouldSuppressDamageGcds(healingEnabled, lowestHp, healing));
    }

    [Fact]
    public void IsEmergencyHp_TrueAtOrBelowThreshold()
    {
        var healing = new HealingConfig { GcdEmergencyThreshold = 0.40f };
        Assert.True(HealingUrgency.IsEmergencyHp(0.40f, healing));
        Assert.False(HealingUrgency.IsEmergencyHp(0.41f, healing));
    }
}
