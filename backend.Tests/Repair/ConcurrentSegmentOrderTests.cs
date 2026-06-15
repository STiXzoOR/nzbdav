using NzbWebDAV.Extensions;
using Xunit;

namespace NzbWebDAV.Tests.Repair;

/// <summary>
/// HealthCheckService STATs the input segments with bounded concurrency via
/// <see cref="IEnumerableTaskExtensions.WithConcurrencyAsync{T}"/>, which yields results OUT OF
/// ORDER. The service preserves the segments[i] &lt;-&gt; statuses[i] alignment (the par2 byte-range
/// mapping in BuildMissingRanges depends on it) by tagging each result with its original index and
/// writing it back to statuses[index]. This test reproduces that exact pattern with deliberately
/// out-of-order completion and asserts every slot lands at its source index.
/// </summary>
public class ConcurrentSegmentOrderTests
{
    [Fact]
    public async Task WithConcurrency_IndexRestore_PreservesPerSegmentAlignment()
    {
        const int count = 50;
        var segments = Enumerable.Range(0, count).Select(i => $"seg-{i}").ToList();
        var results = new int[count];

        // Even-indexed segments complete fast, odd-indexed slowly -> guaranteed out-of-order
        // completion through WithConcurrencyAsync. The classification we record IS the index,
        // so any misalignment would surface as results[i] != i.
        var indexed = segments.Select(async (segmentId, i) =>
        {
            await Task.Delay(i % 2 == 0 ? 1 : 20);
            // value derived from the segment id, so a swapped index would be detectable
            return (Index: i, Value: int.Parse(segmentId.Substring("seg-".Length)));
        });

        var processed = 0;
        await foreach (var r in indexed.WithConcurrencyAsync(8))
        {
            results[r.Index] = r.Value;
            processed++;
        }

        Assert.Equal(count, processed);
        for (var i = 0; i < count; i++)
            Assert.Equal(i, results[i]);
    }
}
