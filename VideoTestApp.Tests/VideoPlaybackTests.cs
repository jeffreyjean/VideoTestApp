using System.IO;
using System.Windows.Threading;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.UI;
using Xunit;

namespace VideoTestApp.Tests;

public sealed class VideoPlaybackTests
{
    [Fact]
    public async Task WindowClosesCleanlyBeforeOpeningVideo()
    {
        await OnDispatcherAsync(async dispatcher =>
        {
            var window = new MainWindow(StartupOptions.Parse([]), MediaLibrary.DefaultFolder);
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            try
            {
                window.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public async Task PlaysH264ReportsMetadataAndRendersFrames()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "h264.mp4");
        try
        {
            var fileFps = await CreateVideoAsync(directory);
            await OnDispatcherAsync(async dispatcher =>
            {
                await using var playback = new VideoPlayback(dispatcher);
                var opened = new TaskCompletionSource<VideoInfo>();
                var framesReady = new TaskCompletionSource();
                var seekFrameReady = new TaskCompletionSource();
                var failure = new TaskCompletionSource<string>();
                var frameCount = 0;
                var checkingSeek = false;
                var sawBlueFrame = false;
                playback.Opened += info => opened.TrySetResult(info);
                playback.Failed += message => failure.TrySetResult(message);
                playback.FrameRendered += () =>
                {
                    var pixel = playback.ReadPixel(160, 90);
                    if (++frameCount == 10)
                    {
                        sawBlueFrame = pixel[0] > 150 && pixel[2] < 80;
                        framesReady.TrySetResult();
                    }

                    if (checkingSeek && pixel[2] > 150 && pixel[0] < 80)
                    {
                        seekFrameReady.TrySetResult();
                    }
                };
                await playback.OpenAsync(path);
                var openResult = await Task.WhenAny(opened.Task, failure.Task)
                    .WaitAsync(TimeSpan.FromSeconds(20));
                Assert.True(openResult == opened.Task,
                    failure.Task.IsCompletedSuccessfully ? failure.Task.Result : "Video did not open.");
                var info = await opened.Task;
                Assert.Equal(320, info.Width);
                Assert.Equal(180, info.Height);
                Assert.NotNull(info.SourceFps);
                Assert.InRange(info.SourceFps.Value, fileFps - 0.01, fileFps + 0.01);
                playback.Volume = 0;
                playback.Play();
                var frameResult = await Task.WhenAny(framesReady.Task, failure.Task)
                    .WaitAsync(TimeSpan.FromSeconds(20));
                Assert.True(frameResult == framesReady.Task,
                    failure.Task.IsCompletedSuccessfully ? failure.Task.Result : "Video did not render.");
                Assert.True(sawBlueFrame, "Expected the first clip's blue pixels.");
                Assert.True(playback.CanSeek);
                playback.Pause();
                checkingSeek = true;
                playback.Position = TimeSpan.FromSeconds(2.5);
                Assert.InRange(playback.Position.TotalSeconds, 2.3, 2.7);
                playback.Play();
                var seekResult = await Task.WhenAny(seekFrameReady.Task, failure.Task)
                    .WaitAsync(TimeSpan.FromSeconds(20));
                Assert.True(seekResult == seekFrameReady.Task,
                    failure.Task.IsCompletedSuccessfully ? failure.Task.Result : "Seek did not render the second clip.");
                Assert.False(failure.Task.IsCompleted);
            });
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public async Task InvalidMediaReportsFailureInsteadOfPretendingToPlay()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "invalid.mp4");
        try
        {
            await File.WriteAllTextAsync(path, "This is not a video.");
            await OnDispatcherAsync(async dispatcher =>
            {
                await using var playback = new VideoPlayback(dispatcher);
                var failure = new TaskCompletionSource<string>();
                playback.Failed += message => failure.TrySetResult(message);
                await playback.OpenAsync(path);
                var message = await failure.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Contains("Windows playback failed", message);
                Assert.Contains("HRESULT", message);
            });
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public async Task StressPlaysEveryMediaFileOnceThenExitsWithFailureCode()
    {
        var directory = CreateTestDirectory();
        try
        {
            await CreateVideoAsync(directory, "a.mp4");
            await File.WriteAllTextAsync(Path.Combine(directory, "b.mp4"), "This is not a video.");
            await File.WriteAllTextAsync(Path.Combine(directory, "notes.txt"), "Not media.");

            var (exitCode, log) = await RunStressAsync(directory, loop: false);

            Assert.Equal(1, exitCode);
            Assert.Contains("PASS  a.mp4", log);
            Assert.Contains("FAIL  b.mp4", log);
            Assert.DoesNotContain("notes.txt", log);
            Assert.Contains("Pass 1 complete: 1 passed, 1 failed.", log);
            Assert.DoesNotContain("Pass 2", log);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public async Task StressExitsWithZeroWhenEveryFilePasses()
    {
        var directory = CreateTestDirectory();
        try
        {
            await CreateVideoAsync(directory, "a.mp4");

            var (exitCode, log) = await RunStressAsync(directory, loop: false);

            Assert.Equal(0, exitCode);
            Assert.Contains("Every media file played once.", log);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public async Task StressExitsWithFailureCodeWhenMediaFolderHasNoVideos()
    {
        var directory = CreateTestDirectory();
        try
        {
            var (exitCode, log) = await RunStressAsync(directory, loop: false);

            Assert.Equal(1, exitCode);
            Assert.Contains("No video files were found", log);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public async Task TimedStressLoopExitsBeforeCurrentVideoEnds()
    {
        var directory = CreateTestDirectory();
        try
        {
            await CreateVideoAsync(directory, "a.mp4");

            var (exitCode, log) = await RunStressAsync(directory, loop: true, loopMinutes: "0.01");

            Assert.Equal(0, exitCode);
            Assert.Contains("Loop duration reached (0.01 minutes).", log);
            Assert.DoesNotContain("PASS  a.mp4", log);
            Assert.DoesNotContain("Application closed.", log);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public async Task StressLoopKeepsPlayingUntilClosed()
    {
        var directory = CreateTestDirectory();
        try
        {
            await CreateVideoAsync(directory, "a.mp4");

            var (exitCode, log) = await RunStressAsync(directory, loop: true,
                closeWhenLogContains: "Pass 2, file 1/1");

            Assert.Equal(0, exitCode);
            Assert.Contains("Pass 1 complete: 1 passed, 0 failed.", log);
            Assert.Contains("Stress run ended: Application closed.", log);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    private static async Task<(int ExitCode, string Log)> RunStressAsync(
        string mediaFolder, bool loop, string? closeWhenLogContains = null, string? loopMinutes = null)
    {
        var exitCode = -1;
        string? logPath = null;
        await OnDispatcherAsync(async _ =>
        {
            var args = new List<string>();
            if (loop)
            {
                args.Add("-Loop");
                if (loopMinutes is not null)
                {
                    args.Add(loopMinutes);
                }
            }
            else
            {
                args.Add("-Stress");
            }

            var window = new MainWindow(StartupOptions.Parse(args), mediaFolder);
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            if (closeWhenLogContains is not null)
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (window.StressLogPath is null || !ReadShared(window.StressLogPath).Contains(closeWhenLogContains))
                {
                    Assert.True(DateTime.UtcNow < deadline, $"Log never contained '{closeWhenLogContains}'.");
                    await Task.Delay(100);
                }

                window.Close();
            }

            await closed.Task.WaitAsync(TimeSpan.FromSeconds(45));
            exitCode = window.ExitCode;
            logPath = window.StressLogPath;
        });

        Assert.NotNull(logPath);
        var log = ReadShared(logPath);
        File.Delete(logPath);
        return (exitCode, log);
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<double> CreateVideoAsync(string directory, string fileName = "h264.mp4")
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.FailIfExists);
        var composition = new MediaComposition();
        composition.Clips.Add(MediaClip.CreateFromColor(
            Color.FromArgb(255, 30, 120, 220), TimeSpan.FromSeconds(2)));
        composition.Clips.Add(MediaClip.CreateFromColor(
            Color.FromArgb(255, 220, 100, 30), TimeSpan.FromSeconds(2)));
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
        profile.Video.Width = 320;
        profile.Video.Height = 180;
        profile.Video.FrameRate.Numerator = 30;
        profile.Video.FrameRate.Denominator = 1;
        var result = await composition.RenderToFileAsync(
            file, MediaTrimmingPreference.Precise, profile);
        Assert.Equal(TranscodeFailureReason.None, result);
        var properties = await file.Properties.RetrievePropertiesAsync(["System.Video.FrameRate"]);
        var frameRate = Assert.IsType<uint>(properties["System.Video.FrameRate"]);
        Assert.True(frameRate > 0);
        return frameRate / 1000.0;
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(@"C:\temp", "VideoTestApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTestDirectory(string directory)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            File.Delete(file);
        }

        Directory.Delete(directory);
    }

    private static async Task OnDispatcherAsync(Func<Dispatcher, Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await action(dispatcher);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
