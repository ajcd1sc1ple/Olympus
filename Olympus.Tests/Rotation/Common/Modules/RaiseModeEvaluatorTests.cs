using Olympus.Config;
using Olympus.Rotation.Common.Modules;
using Xunit;

namespace Olympus.Tests.Rotation.Common.Modules;

public class RaiseModeEvaluatorTests
{
    [Fact]
    public void RaiseFirst_AlwaysAllows_PriorityZero()
    {
        var allowed = RaiseModeEvaluator.TryGetRaiseGcdPriority(
            RaiseExecutionMode.RaiseFirst, avgHpPercent: 0.20f, lowestHpPercent: 0.10f,
            out var priority, out var skipReason);

        Assert.True(allowed);
        Assert.Equal(0, priority);
        Assert.Null(skipReason);
    }

    [Fact]
    public void Balanced_Skips_WhenLowestBelowCritical()
    {
        var allowed = RaiseModeEvaluator.TryGetRaiseGcdPriority(
            RaiseExecutionMode.Balanced, avgHpPercent: 0.80f, lowestHpPercent: 0.30f,
            out _, out var skipReason);

        Assert.False(allowed);
        Assert.NotNull(skipReason);
    }

    [Fact]
    public void Balanced_Allows_WhenPartyStable_PriorityOne()
    {
        var allowed = RaiseModeEvaluator.TryGetRaiseGcdPriority(
            RaiseExecutionMode.Balanced, avgHpPercent: 0.85f, lowestHpPercent: 0.55f,
            out var priority, out var skipReason);

        Assert.True(allowed);
        Assert.Equal(1, priority);
        Assert.Null(skipReason);
    }

    [Fact]
    public void HealFirst_Skips_WhenAverageLow()
    {
        var allowed = RaiseModeEvaluator.TryGetRaiseGcdPriority(
            RaiseExecutionMode.HealFirst, avgHpPercent: 0.60f, lowestHpPercent: 0.55f,
            out _, out var skipReason);

        Assert.False(allowed);
        Assert.NotNull(skipReason);
    }

    [Fact]
    public void HealFirst_Allows_WhenStable_WithDeferredPriority()
    {
        var allowed = RaiseModeEvaluator.TryGetRaiseGcdPriority(
            RaiseExecutionMode.HealFirst, avgHpPercent: 0.85f, lowestHpPercent: 0.60f,
            out var priority, out var skipReason);

        Assert.True(allowed);
        Assert.Equal(18, priority);
        Assert.Null(skipReason);
    }

    [Theory]
    [InlineData(RaiseExecutionMode.RaiseFirst, 0)]
    [InlineData(RaiseExecutionMode.Balanced, 1)]
    [InlineData(RaiseExecutionMode.HealFirst, 1)]
    public void PrepPriority_RaiseFirstIsHighest(RaiseExecutionMode mode, int expected)
    {
        Assert.Equal(expected, RaiseModeEvaluator.GetRaisePrepPriority(mode));
    }
}
