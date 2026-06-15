using Xunit;
using NzbWebDAV.Par2Recovery;

namespace NzbWebDAV.Tests.Par2;

public class Par2ParseTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Par2", "fixtures", name);

    [Fact]
    public async Task ReadsSliceSizeAndSourceFile()
    {
        await using var stream = File.OpenRead(Fixture("fixture.par2"));
        var model = await Par2RecoveryModel.ParseAsync(stream, CancellationToken.None);
        Assert.NotNull(model);
        Assert.Equal(4096, model!.SliceSize);
        Assert.Contains(model.SourceFiles, f => f.FileName == "sample.bin");
        Assert.Equal((int)Math.Ceiling(200000.0 / 4096),
            model.SourceFiles.Single(f => f.FileName == "sample.bin").SliceCount);
    }
}
