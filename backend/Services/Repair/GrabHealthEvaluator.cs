namespace NzbWebDAV.Services.Repair;

/// <summary>
/// Grab-time (queue-completion) article-existence gate. Shares the repair job's
/// flap-tolerant, all-provider semantics so the two paths agree:
///
///   - Any segment Inconclusive (provider timeout/transient/fault) => PASS. A flap on a
///     flaky provider must NEVER fail a grab, otherwise a momentary read-timeout
///     permanently blocklists an otherwise-good release in the arr.
///   - Only an all-provider-CONFIRMED missing ratio above the threshold fails the grab.
///
/// par2 recoverability is deliberately NOT consulted here: nzbdav does not reconstruct
/// missing video articles from par2 at read time, so a release whose video articles are
/// genuinely all-provider-missing is unreadable regardless of par2 coverage — failing it
/// at grab time (so the arr grabs a different copy) is the correct outcome. par2 stays
/// where it is meaningful: the repair job's keep-vs-delete decision.
/// </summary>
public static class GrabHealthEvaluator
{
    /// <summary>
    /// Returns null if the release passes the grab-time gate; otherwise the message-id of the
    /// first definitively-missing segment to fail the queue item on.
    /// </summary>
    public static string? FirstFatalSegment(
        IReadOnlyList<(string SegmentId, SegmentStatus Status)> results, double maxMissingRatio)
    {
        var statuses = results.Select(r => r.Status).ToList();
        var verdict = SegmentRatioEvaluator.Evaluate(statuses, maxMissingRatio);
        if (verdict != FileHealthVerdict.DefinitivelyMissing) return null;
        // A DefinitivelyMissing verdict guarantees at least one such segment with zero inconclusives.
        return results.First(r => r.Status == SegmentStatus.DefinitivelyMissing).SegmentId;
    }
}
