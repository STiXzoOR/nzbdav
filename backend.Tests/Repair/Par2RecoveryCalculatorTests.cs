using Xunit;
using NzbWebDAV.Services.Repair;

namespace NzbWebDAV.Tests.Repair;

public class Par2RecoveryCalculatorTests
{
    private const long Slice = 4096;

    [Fact]
    public void NoMissing_IsRecoverable()
    {
        var dmg = Par2RecoveryCalculator.CountDamagedSlices(
            new[] { new Par2RecoveryCalculator.MissingRange(firstSliceIndex: 0, sliceSize: Slice, byteStart: 0, byteLen: 0) }
                .Where(_ => false).ToArray());
        Assert.Equal(0, dmg);
    }

    [Fact]
    public void OneSegmentSpanningTwoSlices_CountsTwo()
    {
        // segment covers bytes [4000, 4000+200) -> slices floor(4000/4096)=0 and floor(4199/4096)=1
        var dmg = Par2RecoveryCalculator.CountDamagedSlices(new[]
        {
            new Par2RecoveryCalculator.MissingRange(firstSliceIndex: 0, sliceSize: Slice, byteStart: 4000, byteLen: 200),
        });
        Assert.Equal(2, dmg);
    }

    [Fact]
    public void OverlappingRangesDeduped()
    {
        var ranges = new[]
        {
            new Par2RecoveryCalculator.MissingRange(0, Slice, 0, 4096),       // slice 0
            new Par2RecoveryCalculator.MissingRange(0, Slice, 100, 50),       // slice 0 again
        };
        Assert.Equal(1, Par2RecoveryCalculator.CountDamagedSlices(ranges));
    }

    [Fact]
    public void FirstSliceIndexOffsetsGlobalIndex()
    {
        var ranges = new[]
        {
            new Par2RecoveryCalculator.MissingRange(firstSliceIndex: 10, sliceSize: Slice, byteStart: 0, byteLen: 1),
        };
        Assert.Equal(1, Par2RecoveryCalculator.CountDamagedSlices(ranges));
    }

    [Fact]
    public void Recoverable_WhenDamageWithinAvailableBlocks()
        => Assert.True(Par2RecoveryCalculator.IsRecoverable(damagedSlices: 3, availableRecoveryBlocks: 4));

    [Fact]
    public void Unrecoverable_WhenDamageExceedsBlocks()
        => Assert.False(Par2RecoveryCalculator.IsRecoverable(damagedSlices: 5, availableRecoveryBlocks: 4));

    [Fact]
    public void PartialVolume_ContributesZero()
    {
        var blocks = Par2RecoveryCalculator.AvailableRecoveryBlocks(new[]
        {
            (BlockCount: 4, AllArticlesPresent: true),
            (BlockCount: 8, AllArticlesPresent: false),
        });
        Assert.Equal(4, blocks);
    }
}
