using Xunit;
using NzbWebDAV.Services.Repair;

namespace NzbWebDAV.Tests.Repair;

public class GrabHealthEvaluatorTests
{
    private static List<(string SegmentId, SegmentStatus Status)> Make(
        int present, int missing, int inconclusive)
    {
        var list = new List<(string, SegmentStatus)>();
        for (var i = 0; i < present; i++) list.Add(($"p{i}", SegmentStatus.Present));
        for (var i = 0; i < missing; i++) list.Add(($"m{i}", SegmentStatus.DefinitivelyMissing));
        for (var i = 0; i < inconclusive; i++) list.Add(($"i{i}", SegmentStatus.Inconclusive));
        return list;
    }

    [Fact]
    public void AllPresent_Passes()
        => Assert.Null(GrabHealthEvaluator.FirstFatalSegment(Make(10, 0, 0), maxMissingRatio: 0.01));

    [Fact]
    public void EmptyList_Passes()
        => Assert.Null(GrabHealthEvaluator.FirstFatalSegment(Make(0, 0, 0), maxMissingRatio: 0.01));

    [Fact]
    public void MissingBelowThreshold_Passes()
        // 5 / 1005 = 0.49% < 1%
        => Assert.Null(GrabHealthEvaluator.FirstFatalSegment(Make(1000, 5, 0), maxMissingRatio: 0.01));

    [Fact]
    public void AnyInconclusive_FlapTolerant_Passes()
        // a provider flap/timeout on even one segment must NEVER fail the grab,
        // even when other segments are confirmed missing above the ratio.
        => Assert.Null(GrabHealthEvaluator.FirstFatalSegment(Make(50, 40, 1), maxMissingRatio: 0.01));

    [Fact]
    public void ConfirmedMissingAboveThreshold_ReturnsFirstMissingSegment()
    {
        // 5 / 105 = ~4.8% > 1%, no inconclusive => genuinely, all-provider-confirmed dead.
        var fatal = GrabHealthEvaluator.FirstFatalSegment(Make(100, 5, 0), maxMissingRatio: 0.01);
        Assert.Equal("m0", fatal);
    }
}
