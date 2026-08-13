using Olympus.Config;
using Xunit;

namespace Olympus.Tests.Config;

public class ResurrectionConfigTests
{
    [Theory]
    [InlineData(0.05f, 0.10f)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(0.75f, 0.50f)]
    public void RaiseMpThreshold_ClampsToUiRange(float input, float expected)
    {
        var config = new ResurrectionConfig { RaiseMpThreshold = input };
        Assert.Equal(expected, config.RaiseMpThreshold);
    }
}
