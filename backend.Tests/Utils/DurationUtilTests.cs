using Xunit;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class DurationUtilTests
{
    [Theory]
    [InlineData("24h", 24 * 3600)]
    [InlineData("8h", 8 * 3600)]
    [InlineData("7d", 7 * 24 * 3600)]
    [InlineData("30m", 30 * 60)]
    [InlineData("45s", 45)]
    public void ParsesSuffixedDurations(string input, int expectedSeconds)
        => Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DurationUtil.Parse(input));

    [Fact]
    public void FallsBackToDefaultOnGarbage()
        => Assert.Equal(TimeSpan.FromHours(1), DurationUtil.Parse("nonsense", TimeSpan.FromHours(1)));
}
