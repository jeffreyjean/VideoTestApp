using Xunit;

namespace VideoTestApp.Tests;

public sealed class FrameRateMeterTests
{
    [Theory]
    [InlineData(24)]
    [InlineData(30)]
    [InlineData(60)]
    public void MeasuresDeliveredFramesDuringWarmup(int fps)
    {
        var meter = new FrameRateMeter(TimeSpan.FromSeconds(2));
        meter.Reset(TimeSpan.Zero);
        for (var frame = 1; frame <= fps; frame++)
        {
            meter.RecordFrame(TimeSpan.FromSeconds((double)frame / fps));
        }

        Assert.Equal(fps, meter.GetFramesPerSecond(TimeSpan.FromSeconds(1)), 6);
    }

    [Fact]
    public void RollingWindowExcludesOldFramesAndDecaysToZeroAfterStall()
    {
        var meter = new FrameRateMeter(TimeSpan.FromSeconds(2));
        meter.Reset(TimeSpan.Zero);
        for (var frame = 1; frame <= 120; frame++)
        {
            meter.RecordFrame(TimeSpan.FromSeconds(frame / 30.0));
        }

        Assert.Equal(30, meter.GetFramesPerSecond(TimeSpan.FromSeconds(4)), 6);
        Assert.Equal(15, meter.GetFramesPerSecond(TimeSpan.FromSeconds(5)), 6);
        Assert.Equal(0, meter.GetFramesPerSecond(TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public void ResetExcludesFramesBeforePauseOrSeek()
    {
        var meter = new FrameRateMeter(TimeSpan.FromSeconds(2));
        meter.RecordFrame(TimeSpan.FromSeconds(1));
        meter.Reset(TimeSpan.FromSeconds(10));
        Assert.Equal(0, meter.GetFramesPerSecond(TimeSpan.FromSeconds(10)));

        meter.RecordFrame(TimeSpan.FromSeconds(10.5));
        Assert.Equal(2, meter.GetFramesPerSecond(TimeSpan.FromSeconds(10.5)), 6);
    }
}
