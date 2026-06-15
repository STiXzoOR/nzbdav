using Xunit;
using NzbWebDAV.Services.Repair;

namespace NzbWebDAV.Tests.Repair;

public class StrikeMachineTests
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private static readonly TimeSpan Backoff = TimeSpan.FromHours(8);
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Healthy_ResetsCountAndClearsFirstFailed()
    {
        var s = StrikeMachine.Next(FileHealthVerdict.Healthy, count: 2, firstFailed: T0,
            now: T0.AddHours(30), window: Window, backoff: Backoff, requiredFailures: 3);
        Assert.Equal(0, s.NewCount);
        Assert.Null(s.NewFirstFailed);
        Assert.False(s.ShouldRepair);
    }

    [Fact]
    public void Inconclusive_PreservesCount_NoRepair()
    {
        var s = StrikeMachine.Next(FileHealthVerdict.Inconclusive, count: 2, firstFailed: T0,
            now: T0.AddHours(30), window: Window, backoff: Backoff, requiredFailures: 3);
        Assert.Equal(2, s.NewCount);
        Assert.Equal(T0, s.NewFirstFailed);
        Assert.False(s.ShouldRepair);
    }

    [Fact]
    public void FirstMissing_SetsFirstFailed_NoRepairYet()
    {
        var s = StrikeMachine.Next(FileHealthVerdict.DefinitivelyMissing, count: 0, firstFailed: null,
            now: T0, window: Window, backoff: Backoff, requiredFailures: 3);
        Assert.Equal(1, s.NewCount);
        Assert.Equal(T0, s.NewFirstFailed);
        Assert.False(s.ShouldRepair);
    }

    [Fact]
    public void EnoughStrikesButWindowNotElapsed_NoRepair()
    {
        var s = StrikeMachine.Next(FileHealthVerdict.DefinitivelyMissing, count: 2, firstFailed: T0,
            now: T0.AddHours(1), window: Window, backoff: Backoff, requiredFailures: 3);
        Assert.Equal(3, s.NewCount);
        Assert.False(s.ShouldRepair);
    }

    [Fact]
    public void EnoughStrikesAndWindowElapsed_Repairs()
    {
        var s = StrikeMachine.Next(FileHealthVerdict.DefinitivelyMissing, count: 2, firstFailed: T0,
            now: T0.AddHours(25), window: Window, backoff: Backoff, requiredFailures: 3);
        Assert.Equal(3, s.NewCount);
        Assert.True(s.ShouldRepair);
    }
}
