using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Playback;

namespace VideoTestApp;

public partial class MainWindow : Window
{
    private static readonly TimeSpan StressOpenTimeout = TimeSpan.FromSeconds(30);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly FrameRateMeter _fps = new(TimeSpan.FromSeconds(2));
    private readonly DispatcherTimer _timer;
    private readonly StartupOptions _options;
    private readonly string _mediaFolder;
    private readonly Stopwatch _filePlayTime = new();
    private VideoPlayback? _playback;
    private VideoInfo? _info;
    private StressRun? _stress;
    private TimeSpan _stressFileStartedAt;
    private bool _stressFileDone;
    private int _fileFrames;
    private bool _opening;
    private bool _seeking;
    private bool _updatingSeek;
    private bool _closing;
    private bool _closed;
    private bool _ready;
    private bool _ended;

    public MainWindow(StartupOptions options, string mediaFolder)
    {
        _options = options;
        _mediaFolder = mediaFolder;
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();
        Loaded += async (_, _) =>
        {
            _ = ShowSystemInfoAsync();
            if (_options.Warnings.Count > 0)
            {
                SetStatus(string.Join(" ", _options.Warnings), true);
            }

            if (_options.Stress)
            {
                StartStress();
            }
            else if (_options.FilePath is not null)
            {
                await OpenVideoAsync(_options.FilePath);
            }
        };
    }

    private async Task ShowSystemInfoAsync()
    {
        SystemInfo info;
        try
        {
            info = await Task.Run(SystemInfo.Collect);
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException
                                          or System.Security.SecurityException)
        {
            WindowsNameText.Text = $"System information unavailable: {exception.Message}";
            GpuPanel.Children.Clear();
            HevcPanel.Children.Clear();
            return;
        }

        WindowsNameText.Text = info.WindowsName;
        WindowsVersionText.Text = info.WindowsVersion;

        GpuPanel.Children.Clear();
        var hardware = info.Adapters.Where(adapter => adapter.IsHardware).ToList();
        if (hardware.Count == 0)
        {
            AddInfoLine(GpuPanel, "No hardware GPU detected", bold: true);
            var software = info.Adapters.FirstOrDefault();
            AddInfoLine(GpuPanel, $"Render adapter: {software?.Name ?? "none"}");
            AddInfoLine(GpuPanel, "Expected video decoding: software (CPU)", warning: true);
        }
        else
        {
            for (var index = 0; index < hardware.Count; index++)
            {
                AddInfoLine(GpuPanel, hardware[index].Name, bold: true, top: index == 0 ? 0 : 8);
                AddInfoLine(GpuPanel, $"Driver version: {hardware[index].DriverVersion ?? "unknown"}");
            }

            AddInfoLine(GpuPanel, "Expected video decoding: GPU hardware, when the GPU supports the codec", top: 8);
        }

        HevcPanel.Children.Clear();
        if (info.HevcPackages.Count == 0)
        {
            AddInfoLine(HevcPanel, "Not installed - HEVC (H.265) playback may fail", warning: true);
        }

        foreach (var package in info.HevcPackages)
        {
            AddInfoLine(HevcPanel, package.Name, bold: true);
            AddInfoLine(HevcPanel, $"Version {package.Version}");
        }
    }

    private static void AddInfoLine(Panel panel, string text, bool bold = false, bool warning = false, double top = 0)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = bold ? 14 : 12,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = warning ? Brushes.Gold : bold ? Brushes.White : (Brush)new BrushConverter().ConvertFrom("#CBD5E1")!,
            Margin = new Thickness(0, top, 0, 0)
        });
    }

    /// <summary>Process exit code: 0 when a stress run had no failures; 1 otherwise.</summary>
    public int ExitCode { get; private set; }

    public string? StressLogPath { get; private set; }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a test video",
            Filter = "Video files|" + string.Join(";", MediaLibrary.VideoExtensions.Select(ext => "*" + ext)) +
                     "|All files|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenVideoAsync(dialog.FileName);
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_stress is null && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            await OpenVideoAsync(paths[0]);
        }
    }

    private async Task OpenVideoAsync(string path)
    {
        if (_opening || _closing)
        {
            return;
        }

        _opening = true;
        OpenButton.IsEnabled = false;
        SetControls(false);
        _ready = false;
        _ended = false;
        _info = null;
        _fps.Reset(_clock.Elapsed);
        VideoImage.Source = null;
        EmptyMessage.Text = "Open or drop a local test video";
        EmptyMessage.Visibility = Visibility.Visible;
        FileNameText.Text = path;
        VideoDetails.Text = "Opening...";
        FpsOverlay.Text = "Source: -- FPS   |   App-rendered: 0.0 FPS";
        TimeText.Text = "00:00 / 00:00";
        _updatingSeek = true;
        SeekSlider.Value = 0;
        _updatingSeek = false;
        SetStatus("Opening video...");

        try
        {
            var previous = _playback;
            _playback = null;
            if (previous is not null)
            {
                await previous.DisposeAsync();
            }

            if (_closing)
            {
                return;
            }

            var playback = new VideoPlayback(Dispatcher);
            _playback = playback;
            playback.Volume = VolumeSlider.Value;
            playback.IsLooping = LoopCheckBox.IsChecked == true;
            playback.Opened += info =>
            {
                if (!ReferenceEquals(_playback, playback) || _closing)
                {
                    return;
                }

                _info = info;
                _ready = true;
                VideoDetails.Text = $"{info.Codec}  |  {info.Width} x {info.Height}";
                SetControls(true);
                _fps.Reset(_clock.Elapsed);
                playback.Play();
                _filePlayTime.Restart();
                SetStatus("Playing. Source FPS is nominal metadata; app-rendered FPS uses a rolling 2-second window.");
            };
            playback.FrameRendered += bitmap =>
            {
                if (ReferenceEquals(_playback, playback) && _ready && !_closing)
                {
                    VideoImage.Source = bitmap;
                    EmptyMessage.Visibility = Visibility.Collapsed;
                    if (playback.State == MediaPlaybackState.Playing)
                    {
                        _fps.RecordFrame(_clock.Elapsed);
                        _fileFrames++;
                    }
                }
            };
            playback.Failed += message =>
            {
                if (ReferenceEquals(_playback, playback) && !_closing)
                {
                    ShowFailure(message);
                    CompleteStressFile(message);
                }
            };
            playback.Ended += () =>
            {
                if (ReferenceEquals(_playback, playback) && !_closing)
                {
                    _ended = true;
                    _fps.Reset(_clock.Elapsed);
                    SetStatus("Playback ended. Press Play to replay.");
                    CompleteStressFile(null);
                }
            };
            await playback.OpenAsync(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or COMException or ArgumentException or InvalidOperationException)
        {
            if (!_closing)
            {
                var message = $"Could not open video: {exception.Message} (0x{exception.HResult:X8})";
                ShowFailure(message);
                CompleteStressFile(message);
            }
        }
        finally
        {
            _opening = false;
            OpenButton.IsEnabled = !_closing && _stress is null;
        }
    }

    private void StartStress()
    {
        var files = MediaLibrary.FindVideos(_mediaFolder);
        _stress = new StressRun(files, _options.Loop, StressRun.CreateLogPath());
        StressLogPath = _stress.LogPath;
        foreach (var warning in _options.Warnings)
        {
            _stress.Log("WARNING  " + warning);
        }

        OpenButton.IsEnabled = false;
        LoopCheckBox.IsChecked = false;
        LoopCheckBox.IsEnabled = false;

        if (files.Count == 0)
        {
            EndStress($"No video files were found in '{_mediaFolder}'.", completed: false);
            ExitCode = 1;
            Close();
            return;
        }

        AdvanceStress();
    }

    private async void AdvanceStress()
    {
        // A failure can be reported while the previous file is still opening.
        while (_opening)
        {
            await Task.Delay(50);
        }

        if (_stress is null || _closing)
        {
            return;
        }

        var next = _stress.MoveNext();
        if (next is null)
        {
            EndStress("Every media file played once.", completed: true);
            Close();
            return;
        }

        _stressFileDone = false;
        _fileFrames = 0;
        _filePlayTime.Reset();
        _stressFileStartedAt = _clock.Elapsed;
        var open = OpenVideoAsync(next);
        FileNameText.Text = $"[Stress pass {_stress.Pass} | file {_stress.FileNumber}/{_stress.FileCount}]  {next}";
        await open;
    }

    private void CompleteStressFile(string? failure)
    {
        if (_stress is null || _stressFileDone)
        {
            return;
        }

        _stressFileDone = true;
        _filePlayTime.Stop();
        if (failure is null && _fileFrames == 0)
        {
            failure = "No video frames were rendered.";
        }

        if (failure is null)
        {
            var seconds = _filePlayTime.Elapsed.TotalSeconds;
            _stress.RecordPass(_filePlayTime.Elapsed, _info?.SourceFps, seconds > 0 ? _fileFrames / seconds : 0);
        }
        else
        {
            _stress.RecordFailure(failure);
        }

        AdvanceStress();
    }

    private void EndStress(string reason, bool completed)
    {
        if (_stress is null)
        {
            return;
        }

        var stress = _stress;
        _stress = null;
        stress.Finish(reason);
        stress.Dispose();
        ExitCode = stress.TotalFailed == 0 && (completed || stress.Loop) ? 0 : 1;
        OpenButton.IsEnabled = !_closing && !_opening;
        LoopCheckBox.IsEnabled = true;
        StopButton.IsEnabled = _ready;
        SetStatus($"Stress run ended: {reason} Passed: {stress.TotalPassed}, failed: {stress.TotalFailed}. " +
                  $"Log: {stress.LogPath}", stress.TotalFailed > 0);
    }

    private void OnTimerTick()
    {
        if (_stress is not null && !_stressFileDone && !_ready && !_opening &&
            _clock.Elapsed - _stressFileStartedAt > StressOpenTimeout)
        {
            var message = $"Timed out after {StressOpenTimeout.TotalSeconds:0} seconds waiting for the video to open.";
            ShowFailure(message);
            CompleteStressFile(message);
        }

        UpdateTelemetry();
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_ready && _playback is not null)
        {
            if (_ended && _playback.CanSeek)
            {
                _playback.Position = TimeSpan.Zero;
            }

            _ended = false;
            _fps.Reset(_clock.Elapsed);
            _playback.Play();
            _filePlayTime.Start();
            SetStatus("Playing.");
        }
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (_ready && _playback is not null)
        {
            _playback.Pause();
            _filePlayTime.Stop();
            _fps.Reset(_clock.Elapsed);
            SetStatus(_stress is null ? "Paused." : "Stress run paused. Press Play to resume or Stop to end it.");
            UpdateTelemetry();
        }
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        var stoppingStress = _stress is not null;
        EndStress("Stopped by user.", completed: false);
        if (_ready && _playback is not null)
        {
            _playback.Pause();
            if (_playback.CanSeek)
            {
                _playback.Position = TimeSpan.Zero;
            }

            _ended = false;
            _fps.Reset(_clock.Elapsed);
            if (!stoppingStress)
            {
                SetStatus("Stopped.");
            }

            UpdateTelemetry();
        }
    }

    private void OnSeekStarted(object sender, MouseButtonEventArgs e) => _seeking = true;

    private void OnSeekFinished(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        Seek();
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_seeking && !_updatingSeek && _ready)
        {
            Seek();
        }
    }

    private void Seek()
    {
        if (_ready && _playback is { CanSeek: true })
        {
            _playback.Position = TimeSpan.FromSeconds(SeekSlider.Value);
            _ended = false;
            _fps.Reset(_clock.Elapsed);
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_playback is not null)
        {
            _playback.Volume = e.NewValue;
        }
    }

    private void OnLoopChanged(object sender, RoutedEventArgs e)
    {
        if (_playback is not null)
        {
            _playback.IsLooping = LoopCheckBox.IsChecked == true;
        }
    }

    private void UpdateTelemetry()
    {
        if (_closing)
        {
            return;
        }

        var source = _info?.SourceFps is double sourceFps ? sourceFps.ToString("0.###") : "--";
        var playing = _ready && !_ended && _playback?.State == MediaPlaybackState.Playing;
        var rendered = playing ? _fps.GetFramesPerSecond(_clock.Elapsed) : 0;
        FpsOverlay.Text = $"Source: {source} FPS   |   App-rendered: {rendered:0.0} FPS";

        if (_ready && _playback is not null)
        {
            var duration = _playback.Duration;
            var position = _playback.Position;
            TimeText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
            if (!_seeking)
            {
                _updatingSeek = true;
                SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
                SeekSlider.Value = Math.Clamp(position.TotalSeconds, 0, SeekSlider.Maximum);
                _updatingSeek = false;
            }

            SeekSlider.IsEnabled = _playback.CanSeek && duration > TimeSpan.Zero;
        }
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");

    private void SetControls(bool enabled)
    {
        PlayButton.IsEnabled = enabled;
        PauseButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled || _stress is not null;
        SeekSlider.IsEnabled = enabled;
    }

    private void ShowFailure(string message)
    {
        _ready = false;
        _playback?.Pause();
        _fps.Reset(_clock.Elapsed);
        SetControls(false);
        VideoImage.Source = null;
        EmptyMessage.Text = "Playback failed - see details below";
        EmptyMessage.Visibility = Visibility.Visible;
        VideoDetails.Text = "Playback failed";
        SetStatus(message, true);
        UpdateTelemetry();
        Trace.TraceError(message);
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error ? Brushes.Salmon : Brushes.LightGray;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        e.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        _timer.Stop();
        IsEnabled = false;
        EndStress("Application closed.", completed: false);
        if (_playback is not null)
        {
            await _playback.DisposeAsync();
        }

        _closed = true;
        _ = Dispatcher.BeginInvoke(Close);
    }
}
