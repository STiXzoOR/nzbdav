namespace NzbWebDAV.Services.Repair;

public static class Par2RecoveryCalculator
{
    /// <summary>A definitively-missing byte range within one source file.</summary>
    public readonly record struct MissingRange(int firstSliceIndex, long sliceSize, long byteStart, long byteLen);

    /// <summary>Map missing byte ranges to the set of damaged global slice indices and count them.</summary>
    public static int CountDamagedSlices(IReadOnlyCollection<MissingRange> ranges)
    {
        var damaged = new HashSet<long>();
        foreach (var r in ranges)
        {
            if (r.byteLen <= 0 || r.sliceSize <= 0) continue;
            var first = r.byteStart / r.sliceSize;
            var last = (r.byteStart + r.byteLen - 1) / r.sliceSize;
            for (var s = first; s <= last; s++)
                damaged.Add(r.firstSliceIndex + s);
        }
        return damaged.Count;
    }

    public static int AvailableRecoveryBlocks(IEnumerable<(int BlockCount, bool AllArticlesPresent)> volumes)
        => volumes.Where(v => v.AllArticlesPresent).Sum(v => v.BlockCount);

    public static bool IsRecoverable(int damagedSlices, int availableRecoveryBlocks)
        => damagedSlices <= availableRecoveryBlocks;
}
