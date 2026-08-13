using Moq;
using Olympus.Rotation.ApolloCore.Helpers;
using Olympus.Services.Prediction;
using Olympus.Timeline;
using Olympus.Timeline.Models;
using Xunit;

namespace Olympus.Tests.Rotation.ApolloCore.Helpers;

public class TimelineHelperStackPrepTests
{
    private static Configuration ConfigWithTimeline()
    {
        var config = new Configuration();
        config.Timeline.EnableTimelinePredictions = true;
        config.Timeline.TimelineConfidenceThreshold = 0.8f;
        config.Healing.RaidwidePreparationWindow = 5f;
        return config;
    }

    private static Mock<ITimelineService> TimelineWithStack(float secondsUntil)
    {
        var prediction = new MechanicPrediction(secondsUntil, TimelineEntryType.Stack, "BossMod stack (shared)", 0.95f);
        var m = new Mock<ITimelineService>();
        m.Setup(s => s.IsActive).Returns(true);
        m.Setup(s => s.Confidence).Returns(1f);
        m.Setup(s => s.NextStackForGcdHealPrep).Returns((MechanicPrediction?)prediction);
        m.Setup(s => s.NextRaidwideForGcdHealPrep).Returns((MechanicPrediction?)null);
        return m;
    }

    [Fact]
    public void IsStackImminentForGcdHealPrep_WithinWindow_ReturnsTrue()
    {
        var timeline = TimelineWithStack(3f);
        var ok = TimelineHelper.IsStackImminentForGcdHealPrep(
            timeline.Object, ConfigWithTimeline(), out var source);

        Assert.True(ok);
        Assert.Equal("Stack", source);
    }

    [Fact]
    public void IsStackImminentForGcdHealPrep_OutsideWindow_ReturnsFalse()
    {
        var timeline = TimelineWithStack(12f);
        var ok = TimelineHelper.IsStackImminentForGcdHealPrep(
            timeline.Object, ConfigWithTimeline(), out _);

        Assert.False(ok);
    }

    [Fact]
    public void IsAoEShieldPrepImminent_StackOnly_TriggersPrep()
    {
        var timeline = TimelineWithStack(2.5f);
        var detector = new Mock<IBossMechanicDetector>();
        detector.SetupGet(d => d.IsRaidwideImminent).Returns(false);

        var ok = TimelineHelper.IsAoEShieldPrepImminent(
            timeline.Object, detector.Object, ConfigWithTimeline(), out var source);

        Assert.True(ok);
        Assert.Equal("Stack", source);
    }

    [Fact]
    public void IsAoEShieldPrepImminent_RaidwideTakesPrecedenceOverStack()
    {
        var rw = new MechanicPrediction(2f, TimelineEntryType.Raidwide, "BossMod raidwide (timeline)", 0.95f);
        var stack = new MechanicPrediction(3f, TimelineEntryType.Stack, "BossMod stack (shared)", 0.95f);
        var timeline = new Mock<ITimelineService>();
        timeline.Setup(s => s.IsActive).Returns(true);
        timeline.Setup(s => s.Confidence).Returns(1f);
        timeline.Setup(s => s.NextRaidwideForGcdHealPrep).Returns((MechanicPrediction?)rw);
        timeline.Setup(s => s.NextStackForGcdHealPrep).Returns((MechanicPrediction?)stack);

        var ok = TimelineHelper.IsAoEShieldPrepImminent(
            timeline.Object, null, ConfigWithTimeline(), out var source);

        Assert.True(ok);
        Assert.Equal("Timeline", source);
    }
}
