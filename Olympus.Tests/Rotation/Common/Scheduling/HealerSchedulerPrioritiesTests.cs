using Olympus.Rotation.Common.Scheduling;
using Xunit;

namespace Olympus.Tests.Rotation.Common.Scheduling;

public class HealerSchedulerPrioritiesTests
{
    [Fact]
    public void TimelineMitigation_BeatsTypicalHealingPriorities()
    {
        // Benediction/Esuna/single heals sit in the 10–80 band.
        Assert.True(HealerSchedulerPriorities.TimelineMitigation < 10);
        Assert.True(HealerSchedulerPriorities.TimelineMitigation < 50);
        Assert.True(HealerSchedulerPriorities.TimelineMitigation < 80);
    }

    [Fact]
    public void ReactiveMitigation_LosesToHealingBand()
    {
        Assert.True(HealerSchedulerPriorities.ReactiveMitigation > 80);
        Assert.True(HealerSchedulerPriorities.ReactiveMitigation < 285); // damage floor
    }

    [Theory]
    [InlineData(true, 0, 90, 8)]
    [InlineData(true, 1, 90, 9)]
    [InlineData(false, 0, 90, 90)]
    [InlineData(false, 1, 110, 110)]
    public void Mitigation_SelectsBand(bool timeline, int offset, int reactive, int expected)
    {
        Assert.Equal(expected, HealerSchedulerPriorities.Mitigation(timeline, offset, reactive));
    }
}
