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

        var outcomes = await client.StatAllProvidersAsync("seg", CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.Exists, outcomes[0].Result);
    }

    [Fact]
    public async Task NonExistsResponse_MapsToDefinitivelyMissing()
    {
        var client = new FakeNntpClient(_ => new UsenetStatResponse
        {
            ArticleExists = false,
            ResponseCode = 430,
            ResponseMessage = "430 no such article",
        });

        var outcomes = await client.StatAllProvidersAsync("seg", CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.DefinitivelyMissing, outcomes[0].Result);
    }

    [Fact]
    public async Task ThrownNonCancellation_MapsToTransientError()
    {
        var client = new FakeNntpClient(_ => throw new IOException("connection reset"));

        var outcomes = await client.StatAllProvidersAsync("seg", CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ProviderStatOutcome.Kind.TransientError, outcomes[0].Result);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var client = new FakeNntpClient(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.StatAllProvidersAsync("seg", CancellationToken.None));
    }

    /// <summary>Minimal NntpClient that only implements StatAsync; everything else throws.</summary>
    private sealed class FakeNntpClient(Func<SegmentId, UsenetStatResponse> stat) : NntpClient
    {
        public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
            => Task.FromResult(stat(segmentId));

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
