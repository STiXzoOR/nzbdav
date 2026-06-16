using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Streams;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public interface INntpClient : IDisposable
{
    // core methods
    Task ConnectAsync(
        string host, int port, bool useSsl, CancellationToken cancellationToken);

    Task<UsenetResponse> AuthenticateAsync(
        string user, string pass, CancellationToken cancellationToken);

    Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    /// <summary>
    /// STAT a single segment against EVERY enabled provider (including circuit-broken
    /// ones — this is a low-priority background confirmation). Returns one outcome per
    /// provider; a thrown timeout/connection error becomes a TransientError outcome.
    /// Each per-provider STAT is bounded by <paramref name="statTimeout"/> so a hung read
    /// (connection-pool starvation / uncancelled socket) becomes a TransientError instead
    /// of blocking the caller's per-segment loop. The outer <paramref name="ct"/> still
    /// propagates a real cancellation.
    /// </summary>
    Task<IReadOnlyList<NzbWebDAV.Services.Repair.ProviderStatOutcome>> StatAllProvidersAsync(
        SegmentId segmentId, TimeSpan statTimeout, CancellationToken ct);

    Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    Task<UsenetDateResponse> DateAsync(
        CancellationToken cancellationToken);

    // optimized for concurrency
    Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        string segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, UsenetExclusiveConnection connection, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, UsenetExclusiveConnection connection, CancellationToken cancellationToken);

    // helpers
    Task<UsenetYencHeader> GetYencHeadersAsync(
        string segmentId, CancellationToken ct);

    Task<long> GetFileSizeAsync(
        NzbFile file, CancellationToken ct);

    Task<NzbFileStream> GetFileStream(
        NzbFile nzbFile, int articleBufferSize, CancellationToken ct);

    NzbFileStream GetFileStream(
        NzbFile nzbFile, long fileSize, int articleBufferSize);

    NzbFileStream GetFileStream(
        string[] segmentIds, long fileSize, int articleBufferSize);

    Task CheckAllSegmentsAsync(
        IEnumerable<string> segmentIds, int concurrency, TimeSpan statTimeout, double maxMissingRatio,
        IProgress<int>? progress, CancellationToken cancellationToken);
}