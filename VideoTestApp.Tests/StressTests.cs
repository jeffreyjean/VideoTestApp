using System.IO;
using Xunit;

namespace VideoTestApp.Tests;

public sealed class StressTests
{
    [Fact]
    public void ParsesStressAndLoopCaseInsensitively()
    {
        var options = StartupOptions.Parse(["-stress", "-LOOP"]);

        Assert.True(options.Stress);
        Assert.True(options.Loop);
        Assert.Null(options.LoopDuration);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void LoopWithoutStressEnablesInfiniteStress()
    {
        var options = StartupOptions.Parse(["-Loop"]);

        Assert.True(options.Stress);
        Assert.True(options.Loop);
        Assert.Null(options.LoopDuration);
        Assert.Empty(options.Warnings);
    }

    [Theory]
    [InlineData("10", 600)]
    [InlineData("0.5", 30)]
    public void LoopDurationIsParsedInMinutes(string minutes, int seconds)
    {
        var options = StartupOptions.Parse(["-Loop", minutes]);

        Assert.True(options.Stress);
        Assert.True(options.Loop);
        Assert.Equal(TimeSpan.FromSeconds(seconds), options.LoopDuration);
        Assert.Null(options.FilePath);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void LoopDoesNotConsumeFollowingStressOption()
    {
        var options = StartupOptions.Parse(["-Loop", "-Stress"]);

        Assert.True(options.Stress);
        Assert.True(options.Loop);
        Assert.Null(options.LoopDuration);
        Assert.Empty(options.Warnings);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e100")]
    [InlineData("1e-100")]
    [InlineData("invalid")]
    public void InvalidLoopDurationDisablesLoopingWithWarning(string value)
    {
        var options = StartupOptions.Parse(["-Loop", value]);

        Assert.True(options.Stress);
        Assert.False(options.Loop);
        Assert.Null(options.LoopDuration);
        Assert.Null(options.FilePath);
        Assert.Contains(options.Warnings, warning => warning.Contains("Invalid -Loop duration"));
    }

    [Fact]
    public void FindsVideosRecursivelyInPathOrderAndIgnoresOtherFiles()
    {
        var folder = Path.Combine(@"C:\temp", "VideoTestApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Sub"));
        try
        {
            foreach (var name in new[] { "b.MP4", "a.webm", "README.md", @"Sub\c.mkv" })
            {
                File.WriteAllText(Path.Combine(folder, name), "");
            }

            var files = MediaLibrary.FindVideos(folder).Select(file => Path.GetRelativePath(folder, file));

            Assert.Equal(["a.webm", "b.MP4", @"Sub\c.mkv"], files);
            Assert.Empty(MediaLibrary.FindVideos(Path.Combine(folder, "Missing")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StressRunPlaysEachFileOnceOrLoops(bool loop)
    {
        var logPath = Path.Combine(@"C:\temp", "VideoTestApp.Tests", Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using (var run = new StressRun(["a.mp4", "b.mp4"], loop, logPath))
            {
                Assert.Equal("a.mp4", run.MoveNext());
                run.RecordPass(TimeSpan.FromSeconds(2), 30, 29.5);
                Assert.Equal("b.mp4", run.MoveNext());
                run.RecordFailure("Decoder missing.");

                if (loop)
                {
                    Assert.Equal("a.mp4", run.MoveNext());
                    Assert.Equal(2, run.Pass);
                    Assert.False(run.Completed);
                }
                else
                {
                    Assert.Null(run.MoveNext());
                    Assert.Null(run.MoveNext());
                    Assert.True(run.Completed);
                }

                Assert.Equal(1, run.TotalPassed);
                Assert.Equal(1, run.TotalFailed);
            }

            var log = File.ReadAllText(logPath);
            Assert.Contains("PASS  a.mp4  played 2.0s  source 30 FPS  avg app-rendered 29.5 FPS", log);
            Assert.Contains("FAIL  b.mp4  Decoder missing.", log);
            Assert.Contains("Pass 1 complete: 1 passed, 1 failed.", log);
        }
        finally
        {
            File.Delete(logPath);
        }
    }
}
