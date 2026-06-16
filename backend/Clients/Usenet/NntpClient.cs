using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Services.Repair;
using NzbWebDAV.Streams;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// Abstract base class for NNTP clients with default implementations of utility methods.
/// </summary>
public abstract class NntpClient : INntpClient
{
    public abstract Task ConnectAsync(
        string host, int port, bool useSsl, CancellationToken cancellationToken);

    public abstract Task<UsenetResponse> AuthenticateAsync(
        string user, string pass, CancellationToken cancellationToken);

    public abstract Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    public abstract Task<UsenetDateResponse> DateAsync(
        CancellationToken cancellationToken);

    public abstract void Dispose();

    public virtual async Task<IReadOnlyList<ProviderStatOutcome>> StatAllProvidersAsync(
        SegmentId segmentId, TimeSpan statTimeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(statTimeout);
        try
        {
            var r = await StatAsync(segmentId, timeoutCts.Token).ConfigureAwait(false);
            // Only a genuine 430 means the article is definitively gone. StatAsync does
            // NOT throw on server-fault/auth codes (Unknown/412/420/480/481/482/502/...);
            // it returns them as ResponseType values. Treat all of those as TransientError
            // (rolls up to Inconclusive) so a provider auth lapse or fault never gets
            // misclassified as a confirmed miss -> false library deletion.
            var kind = r.ResponseType switch
            {
                UsenetResponseType.ArticleExists => ProviderStatOutcome.Kind.Exists,
                UsenetResponseType.NoArticleWithThatMessageId => ProviderStatOutcome.Kind.DefinitivelyMissing,
                _ => ProviderStatOutcome.Kind.TransientError,
            };
            return new[] { new ProviderStatOutcome(kind) };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // per-STAT timeout (hung read / pool starvation) -> transient, never blocks the loop
            return new[] { new ProviderStatOutcome(ProviderStatOutcome.Kind.TransientError) };
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            return new[] { new ProviderStatOutcome(ProviderStatOutcome.Kind.TransientError) };
        }
    }

    public virtual Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        string segmentId,
        CancellationToken cancellationToken
    )
    {
        var message = $"{GetType().Name} does not support acquiring exclusive connections.";
        throw new NotSupportedException(message);
    }

    public virtual Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var message = $"{GetType().Name} does not support DecodedBodyAsync with exclusive connections.";
        throw new NotSupportedException(message);
    }

    public virtual Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var message = $"{GetType().Name} does not support DecodedArticleAsync with exclusive connections.";
        throw new NotSupportedException(message);
    }

    public virtual async Task<UsenetYencHeader> GetYencHeadersAsync(string segmentId, CancellationToken ct)
    {
        var decodedBodyResponse = await DecodedBodyAsync(segmentId, ct).ConfigureAwait(false);
        await using var stream = decodedBodyResponse.Stream;
        var headers = await stream.GetYencHeadersAsync(ct).ConfigureAwait(false);
        return headers!;
    }

    public virtual async Task<long> GetFileSizeAsync(NzbFile file, CancellationToken ct)
    {
        if (file.Segments.Count == 0) return 0;
        var headers = await GetYencHeadersAsync(file.Segments[^1].MessageId, ct).ConfigureAwait(false);
        return headers!.PartOffset + headers!.PartSize;
    }

    public virtual async Task<NzbFileStream> GetFileStream(NzbFile nzbFile, int articleBufferSize, CancellationToken ct)
    {
        var segmentIds = nzbFile.GetSegmentIds();
        var fileSize = await GetFileSizeAsync(nzbFile, ct).ConfigureAwait(false);
        return new NzbFileStream(segmentIds, fileSize, this, articleBufferSize);
    }

    public virtual NzbFileStream GetFileStream(NzbFile nzbFile, long fileSize, int articleBufferSize)
    {
        return new NzbFileStream(nzbFile.GetSegmentIds(), fileSize, this, articleBufferSize);
    }

    public virtual NzbFileStream GetFileStream(string[] segmentIds, long fileSize, int articleBufferSize)
    {
        return new NzbFileStream(segmentIds, fileSize, this, articleBufferSize);
    }

    public virtual async Task CheckAllSegmentsAsync
    (
        IEnumerable<string> segmentIds,
        int concurrency,
        TimeSpan statTimeout,
        double maxMissingRatio,
        IProgress<int>? progress,
        CancellationToken cancellationToken
    )
    {
        // STAT every segment across ALL providers (per-STAT timeout => TransientError, never a
        // false miss) and classify. Unlike the old fail-fast path, we must evaluate the WHOLE set
        // before deciding: a single Inconclusive (flap) anywhere flips the verdict to a pass, so we
        // cannot short-circuit on the first missing segment. Order is irrelevant to the ratio verdict.
        var results = new List<(string SegmentId, SegmentStatus Status)>();
        var processed = 0;
        var indexed = segmentIds
            .Select(async segmentId => (
                SegmentId: segmentId,
                Status: SegmentClassifier.Classify(
                    await StatAllProvidersAsync(segmentId, statTimeout, cancellationToken).ConfigureAwait(false))
            ));

        await foreach (var r in indexed.WithConcurrencyAsync(Math.Max(1, concurrency)).ConfigureAwait(false))
        {
            results.Add(r);
            progress?.Report(++processed);
        }

        var fatal = GrabHealthEvaluator.FirstFatalSegment(results, maxMissingRatio);
        if (fatal != null) throw new UsenetArticleNotFoundException(fatal);
    }
}