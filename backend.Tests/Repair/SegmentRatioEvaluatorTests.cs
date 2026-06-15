using Xunit;
using NzbWebDAV.Services.Repair;

namespace NzbWebDAV.Tests.Repair;

public class SegmentRatioEvaluatorTests
{
    private static SegmentStatus[] Make(int present, int missing, int inconclusive)
    {
        var list = new List<SegmentStatus>();
        list.AddRange(Enumerable.Repeat(SegmentStatus.Present, present));
        list.AddRange(Enumerable.Repeat(SegmentStatus.DefinitivelyMissing, missing));
        list.AddRange(Enumerable.Repeat(SegmentStatus.Inconclusive, inconclusive));
        return list.ToArray();
    }

    [Fact]
    public void AnyInconclusive_IsInconclusive()
        => Assert.Equal(FileHealthVerdict.Inconclusive,
            SegmentRatioEvaluator.Evaluate(Make(90, 5, 5), maxMissingRatio: 0.01));

    [Fact]
    public void MissingBelowThreshold_IsHealthy()
        => Assert.Equal(FileHealthVerdict.Healthy,
            SegmentRatioEvaluator.Evaluate(Make(1000, 5, 0), maxMissingRatio: 0.01)); // 0.5% < 1%

    [Fact]
    public void MissingAboveThreshold_IsDefinitivelyMissing()
        => Assert.Equal(FileHealthVerdict.DefinitivelyMissing,
            SegmentRatioEvaluator.Evaluate(Make(100, 5, 0), maxMissingRatio: 0.01)); // ~4.8% > 1%

    [Fact]
    public void AllPresent_IsHealthy()
        => Assert.Equal(FileHealthVerdict.Healthy,
            SegmentRatioEvaluator.Evaluate(Make(10, 0, 0), maxMissingRatio: 0.01));
}
