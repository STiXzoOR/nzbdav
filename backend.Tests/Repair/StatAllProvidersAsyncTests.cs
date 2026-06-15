using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Services.Repair;
using UsenetSharp.Models;
using Xunit;

namespace NzbWebDAV.Tests.Repair;

/// <summary>
/// Exercises the default <see cref="NntpClient.StatAllProvidersAsync"/> mapping
/// (the single-provider base virtual). The MultiProviderNntpClient override is not
/// unit-tested here because its provider list is a List&lt;MultiConnectionNntpClient&gt;
/// (connection-pool-backed concrete type with no clean fake seam); its mapping is
/// identical to the base virtual and is exercised end-to-end in later repair tasks.
/// </summary>
public class StatAllProvidersAsyncTests
{
    [Fact]
    public async Task ArticleExists_MapsToExists()
    {
        var client = new FakeNntpClient(_ => new UsenetStatResponse
        {
            ArticleExists = true,
            ResponseCode = 223,
            ResponseMessage = "223 0 <seg> article exists",
        });

        var outcomes = await client.StatAllProvidersAsync("seg", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.Exists, outcomes[0].Result);
    }

    [Fact]
    public async Task NoArticleWithThatMessageId_430_MapsToDefinitivelyMissing()
    {
        // 430 is the ONLY response code that confirms the article is gone.
        var client = new FakeNntpClient(_ => new UsenetStatResponse
        {
            ArticleExists = false,
            ResponseCode = 430,
            ResponseMessage = "430 no such article",
        });

        var outcomes = await client.StatAllProvidersAsync("seg", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.DefinitivelyMissing, outcomes[0].Result);
    }

    // A non-430 non-exists response (server fault / auth/permission code) must NOT be
    // treated as a confirmed miss -- StatAsync returns these as ResponseType values
    // without throwing, and counting them as DefinitivelyMissing is a false-deletion path.
    // These cases FAIL before the fix (old mapping: != ArticleExists => DefinitivelyMissing).
    [Theory]
    [InlineData(502, "502 access permanently forbidden")] // AccessPermanentlyForbidden
    [InlineData(480, "480 authentication required")]       // AuthenticationRequired
    [InlineData(481, "481 authentication rejected")]       // AuthenticationRejected
    [InlineData(412, "412 no newsgroup selected")]         // NoGroupSelected
    [InlineData(420, "420 current article number is invalid")] // CurrentArticleInvalid
    public async Task NonExistsFaultCode_MapsToTransientError(int responseCode, string message)
    {
        var client = new FakeNntpClient(_ => new UsenetStatResponse
        {
            ArticleExists = false,
            ResponseCode = responseCode,
            ResponseMessage = message,
        });

        var outcomes = await client.StatAllProvidersAsync("seg", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.TransientError, outcomes[0].Result);
    }

    [Fact]
    public async Task ThrownNonCancellation_MapsToTransientError()
    {
        var client = new FakeNntpClient(_ => throw new IOException("connection reset"));

        var outcomes = await client.StatAllProvidersAsync("seg", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.TransientError, outcomes[0].Result);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var client = new FakeNntpClient(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.StatAllProvidersAsync("seg", TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    // A hung STAT (uncancelled socket read / connection-pool starvation) must NOT block the loop:
    // the per-STAT timeout cancels the inner read and classifies it as TransientError, fast.
    [Fact]
    public async Task PerStatTimeout_RoutesToTransientError_DoesNotHang()
    {
        // StatAsync blocks forever (until its token is cancelled) -- simulates a hung NNTP read.
        var client = new FakeNntpClient(stat: null,
            statAsync: async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new UsenetStatResponse { ArticleExists = true, ResponseCode = 223, ResponseMessage = "223" };
            });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var outcomes = await client.StatAllProvidersAsync(
            "seg", TimeSpan.FromMilliseconds(100), CancellationToken.None);
        sw.Stop();

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.TransientError, outcomes[0].Result);
        // Generous upper bound: proves it returned via the timeout, not hung.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"StatAllProvidersAsync should return shortly after the 100ms timeout, took {sw.Elapsed}.");
    }

    // The outer ct cancellation must still propagate even with the per-STAT timeout guard in place.
    [Fact]
    public async Task OuterCancellation_StillPropagates_WithTimeout()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeNntpClient(stat: null,
            statAsync: async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new UsenetStatResponse { ArticleExists = true, ResponseCode = 223, ResponseMessage = "223" };
            });

        // Cancel the OUTER token shortly; the linked inner token cancels too, but the
        // `!ct.IsCancellationRequested` guard ensures this rethrows instead of being swallowed.
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // TaskCanceledException derives from OperationCanceledException; ThrowsAny accepts both.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.StatAllProvidersAsync("seg", TimeSpan.FromSeconds(30), cts.Token));
    }

    /// <summary>Minimal NntpClient that only implements StatAsync; everything else throws.</summary>
    private sealed class FakeNntpClient : NntpClient
    {
        private readonly Func<SegmentId, UsenetStatResponse>? _stat;
        private readonly Func<CancellationToken, Task<UsenetStatResponse>>? _statAsync;

        public FakeNntpClient(Func<SegmentId, UsenetStatResponse> stat)
            => _stat = stat;

        public FakeNntpClient(
            Func<SegmentId, UsenetStatResponse>? stat,
            Func<CancellationToken, Task<UsenetStatResponse>> statAsync)
        {
            _stat = stat;
            _statAsync = statAsync;
        }

        public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
            => _statAsync != null ? _statAsync(cancellationToken) : Task.FromResult(_stat!(segmentId));

        public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override void Dispose() { }
    }
}
