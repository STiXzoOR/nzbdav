namespace NzbWebDAV.Services.Repair;

public readonly record struct StrikeResult(
    int NewCount, DateTimeOffset? NewFirstFailed, bool ShouldRepair, DateTimeOffset NextCheck);

public static class StrikeMachine
{
    public static StrikeResult Next(
        FileHealthVerdict verdict, int count, DateTimeOffset? firstFailed, DateTimeOffset now,
        TimeSpan window, TimeSpan backoff, int requiredFailures)
    {
        switch (verdict)
        {
            case FileHealthVerdict.Healthy:
                return new StrikeResult(0, null, false, now + backoff);

            case FileHealthVerdict.Inconclusive:
                return new StrikeResult(count, firstFailed, false, now + backoff);

            case FileHealthVerdict.DefinitivelyMissing:
                var newCount = count + 1;
                var newFirst = firstFailed ?? now;
                var windowElapsed = now - newFirst >= window;
                var shouldRepair = newCount >= requiredFailures && windowElapsed;
                return new StrikeResult(newCount, newFirst, shouldRepair, now + backoff);

            default:
                return new StrikeResult(count, firstFailed, false, now + backoff);
        }
    }
}
