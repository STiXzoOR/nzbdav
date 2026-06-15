using Xunit;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Repair;

namespace NzbWebDAV.Tests.Repair;

public class HealthCheckVerdictTests
{
    private const double MaxMissingRatio = 0.01;

    private static List<SegmentStatus> Statuses(params SegmentStatus[] s) => s.ToList();
    private static List<string> Segments(int n) => Enumerable.Range(0, n).Select(i => $"seg{i}").ToList();

    private static Par2RecoverySet Set(long sliceSize = 1000) =>
        new() { Id = Guid.NewGuid(), SliceSize = sliceSize, TotalRecoveryBlocks = 10 };

    private static Par2SourceFile Source(Guid setId, long fileLength, int firstSlice = 0) =>
        new() { Id = Guid.NewGuid(), RecoverySetId = setId, DavItemId = Guid.NewGuid(), FileLength = fileLength, FirstSliceIndex = firstSlice };

    // ---- no par2 → ratio fallback ----

    [Fact]
    public void NoPar2Set_FallsBackToRatio_AllPresentHealthy()
    {
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.Present, SegmentStatus.Present), Segments(2),
            par2Set: null, sourceRow: null, availableRecoveryBlocks: 0,
            anyVolumeInconclusive: false, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.Healthy, verdict);
    }

    [Fact]
    public void NoPar2Set_FallsBackToRatio_TooManyMissingDefinitelyMissing()
    {
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.DefinitivelyMissing, SegmentStatus.Present), Segments(2),
            par2Set: null, sourceRow: null, availableRecoveryBlocks: 0,
            anyVolumeInconclusive: false, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.DefinitivelyMissing, verdict);
    }

    // ---- inconclusive on input segments dominates ----

    [Fact]
    public void Par2Set_InputInconclusive_DominatesToInconclusive()
    {
        var set = Set();
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.Present, SegmentStatus.Inconclusive), Segments(2),
            par2Set: set, sourceRow: Source(set.Id, 2000), availableRecoveryBlocks: 100,
            anyVolumeInconclusive: false, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.Inconclusive, verdict);
    }

    // ---- par2 set but no source row → ratio fallback ----

    [Fact]
    public void Par2Set_NoSourceRow_FallsBackToRatio()
    {
        var set = Set();
        // 1 of 2 missing → ratio 0.5 > 0.01 → DefinitivelyMissing under fallback
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.DefinitivelyMissing, SegmentStatus.Present), Segments(2),
            par2Set: set, sourceRow: null, availableRecoveryBlocks: 100,
            anyVolumeInconclusive: false, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.DefinitivelyMissing, verdict);
    }

    // ---- recovery-volume inconclusive dominates ----

    [Fact]
    public void Par2Set_VolumeInconclusive_DominatesToInconclusive()
    {
        var set = Set();
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.DefinitivelyMissing, SegmentStatus.Present), Segments(2),
            par2Set: set, sourceRow: Source(set.Id, 2000), availableRecoveryBlocks: 0,
            anyVolumeInconclusive: true, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.Inconclusive, verdict);
    }

    // ---- par2 recoverability ----

    [Fact]
    public void Par2Set_Recoverable_Healthy()
    {
        var set = Set(sliceSize: 1000);
        // file 4000 bytes / 4 segments = 1000/segment; 1 missing → 1 damaged slice
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.DefinitivelyMissing, SegmentStatus.Present, SegmentStatus.Present, SegmentStatus.Present),
            Segments(4),
            par2Set: set, sourceRow: Source(set.Id, 4000), availableRecoveryBlocks: 1,
            anyVolumeInconclusive: false, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.Healthy, verdict);
    }

    [Fact]
    public void Par2Set_NotRecoverable_DefinitivelyMissing()
    {
        var set = Set(sliceSize: 1000);
        // 3 missing of 4 → ~3 damaged slices, only 1 recovery block → not recoverable
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.DefinitivelyMissing, SegmentStatus.DefinitivelyMissing, SegmentStatus.DefinitivelyMissing, SegmentStatus.Present),
            Segments(4),
            par2Set: set, sourceRow: Source(set.Id, 4000), availableRecoveryBlocks: 1,
            anyVolumeInconclusive: false, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.DefinitivelyMissing, verdict);
    }

    [Fact]
    public void Par2Set_AllPresent_NoDamage_Healthy()
    {
        var set = Set(sliceSize: 1000);
        var verdict = HealthCheckService.DecideVerdict(
            Statuses(SegmentStatus.Present, SegmentStatus.Present), Segments(2),
            par2Set: set, sourceRow: Source(set.Id, 2000), availableRecoveryBlocks: 0,
            anyVolumeInconclusive: false, MaxMissingRatio);
        Assert.Equal(FileHealthVerdict.Healthy, verdict);
    }

    // ---- BuildMissingRanges ----

    [Fact]
    public void BuildMissingRanges_EqualSizeApprox_OnlyMissingContributeRanges()
    {
        var set = Set(sliceSize: 1000);
        var src = Source(set.Id, 4000, firstSlice: 5);
        var ranges = HealthCheckService.BuildMissingRanges(
            Segments(4),
            Statuses(SegmentStatus.DefinitivelyMissing, SegmentStatus.Present, SegmentStatus.DefinitivelyMissing, SegmentStatus.Present),
            src, set.SliceSize);

        Assert.Equal(2, ranges.Count);
        // perSegment = 4000/4 = 1000; missing at i=0 (byteStart 0) and i=2 (byteStart 2000)
        Assert.Contains(ranges, r => r.byteStart == 0 && r.byteLen == 1000 && r.firstSliceIndex == 5 && r.sliceSize == 1000);
        Assert.Contains(ranges, r => r.byteStart == 2000 && r.byteLen == 1000 && r.firstSliceIndex == 5 && r.sliceSize == 1000);
    }

    [Fact]
    public void BuildMissingRanges_NoMissing_Empty()
    {
        var set = Set();
        var ranges = HealthCheckService.BuildMissingRanges(
            Segments(3),
            Statuses(SegmentStatus.Present, SegmentStatus.Present, SegmentStatus.Present),
            Source(set.Id, 3000), set.SliceSize);
        Assert.Empty(ranges);
    }

    [Fact]
    public void BuildMissingRanges_ZeroLengthFile_Empty()
    {
        var set = Set();
        var ranges = HealthCheckService.BuildMissingRanges(
            Segments(2),
            Statuses(SegmentStatus.DefinitivelyMissing, SegmentStatus.DefinitivelyMissing),
            Source(set.Id, 0), set.SliceSize);
        Assert.Empty(ranges);
    }
}
