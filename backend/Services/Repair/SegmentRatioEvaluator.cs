namespace NzbWebDAV.Services.Repair;

/// <summary>
/// Fallback verdict when no par2 recovery model exists. Inconclusive dominates:
/// we never delete if any segment could not be confirmed.
/// </summary>
public static class SegmentRatioEvaluator
{
    public static FileHealthVerdict Evaluate(
        IReadOnlyCollection<SegmentStatus> segments, double maxMissingRatio)
    {
        if (segments.Count == 0) return FileHealthVerdict.Inconclusive;
        if (segments.Any(s => s == SegmentStatus.Inconclusive))
            return FileHealthVerdict.Inconclusive;
        var missing = segments.Count(s => s == SegmentStatus.DefinitivelyMissing);
        var ratio = (double)missing / segments.Count;
        return ratio > maxMissingRatio
            ? FileHealthVerdict.DefinitivelyMissing
            : FileHealthVerdict.Healthy;
    }
}
