namespace NzbWebDAV.Services.Repair;

public static class SegmentClassifier
{
    /// <summary>
    /// Roll up per-provider STAT outcomes for ONE segment into a single status.
    /// Present if any provider has it. DefinitivelyMissing only if every provider
    /// returned a definitive 430 with zero transient errors. Otherwise Inconclusive.
    /// </summary>
    public static SegmentStatus Classify(IReadOnlyCollection<ProviderStatOutcome> outcomes)
    {
        if (outcomes.Count == 0) return SegmentStatus.Inconclusive;
        if (outcomes.Any(o => o.Result == ProviderStatOutcome.Kind.Exists))
            return SegmentStatus.Present;
        if (outcomes.All(o => o.Result == ProviderStatOutcome.Kind.DefinitivelyMissing))
            return SegmentStatus.DefinitivelyMissing;
        return SegmentStatus.Inconclusive;
    }
}
