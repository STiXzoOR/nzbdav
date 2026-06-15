using Xunit;
using NzbWebDAV.Services.Repair;

namespace NzbWebDAV.Tests.Repair;

public class SegmentClassifierTests
{
    private static ProviderStatOutcome Exists => new(ProviderStatOutcome.Kind.Exists);
    private static ProviderStatOutcome Missing => new(ProviderStatOutcome.Kind.DefinitivelyMissing);
    private static ProviderStatOutcome Transient => new(ProviderStatOutcome.Kind.TransientError);

    [Fact]
    public void AnyProviderExists_IsPresent()
        => Assert.Equal(SegmentStatus.Present,
            SegmentClassifier.Classify(new[] { Transient, Exists, Missing }));

    [Fact]
    public void AllProvidersMissing_IsDefinitivelyMissing()
        => Assert.Equal(SegmentStatus.DefinitivelyMissing,
            SegmentClassifier.Classify(new[] { Missing, Missing }));

    [Fact]
    public void MissingPlusTransient_IsInconclusive()
        => Assert.Equal(SegmentStatus.Inconclusive,
            SegmentClassifier.Classify(new[] { Missing, Transient }));

    [Fact]
    public void AllTransient_IsInconclusive()
        => Assert.Equal(SegmentStatus.Inconclusive,
            SegmentClassifier.Classify(new[] { Transient, Transient }));

    [Fact]
    public void NoProviders_IsInconclusive()
        => Assert.Equal(SegmentStatus.Inconclusive,
            SegmentClassifier.Classify(Array.Empty<ProviderStatOutcome>()));
}
