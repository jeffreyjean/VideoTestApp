using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SharpGen.Runtime;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using MediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace VideoTestApp;

public sealed record VideoInfo(string Codec, int Width, int Height, double? SourceFps);

public sealed class VideoPlayback : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MediaPlayer _player;
    private readonly D3DImage _image = new();
    private MediaSource? _source;
    private MediaPlaybackItem? _item;
    private SharedVideoSurface? _surface;
    private volatile bool _disposed;
    private int _failed;
    private int _framePending;

    public event Action<VideoInfo>? Opened;
    public event Action? FrameRendered;
    public event Action<string>? Failed;
    public event Action? Ended;

    public VideoPlayback(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _player = new MediaPlayer
        {
            AutoPlay = false,
            IsVideoFrameServerEnabled = true,
            AudioCategory = MediaPlayerAudioCategory.Media
        };
        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.MediaEnded += OnMediaEnded;
        _player.VideoFrameAvailable += OnVideoFrameAvailable;
        _image.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
    }

    /// <summary>The GPU surface that video frames are rendered into.</summary>
    public ImageSource Image => _image;

    public MediaPlaybackState State => _player.PlaybackSession.PlaybackState;
    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;
    public TimeSpan Position
    {
        get => _player.PlaybackSession.Position;
        set => _player.PlaybackSession.Position = value;
    }

    public bool CanSeek => _player.PlaybackSession.CanSeek;
    public double Volume { set => _player.Volume = value; }
    public bool IsLooping { set => _player.IsLoopingEnabled = value; }

    public async Task OpenAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        ObjectDisposedException.ThrowIf(_disposed, this);
        _source = MediaSource.CreateFromStorageFile(file);
        _item = new MediaPlaybackItem(_source);
        _player.Source = _item;
    }

    public void Play() => _player.Play();
    public void Pause() => _player.Pause();

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed || Volatile.Read(ref _failed) != 0)
            {
                return;
            }

            var selected = _item!.VideoTracks.SelectedIndex;
            if (selected < 0 || sender.PlaybackSession.NaturalVideoWidth == 0)
            {
                ReportFailure("The file has no playable video track.");
                return;
            }

            var properties = _item.VideoTracks[selected].GetEncodingProperties();
            var rate = properties.FrameRate;
            double? fps = rate.Numerator > 0 && rate.Denominator > 0
                ? (double)rate.Numerator / rate.Denominator
                : null;
            Opened?.Invoke(new VideoInfo(
                properties.Subtype,
                checked((int)sender.PlaybackSession.NaturalVideoWidth),
                checked((int)sender.PlaybackSession.NaturalVideoHeight),
                fps));
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        ReportFailure(
            $"Windows playback failed: {args.Error}. {args.ErrorMessage} " +
            $"HRESULT: 0x{args.ExtendedErrorCode.HResult:X8}. " +
            "The codec, profile, or container may be unsupported. " +
            "HEVC, AV1, VP9, and MPEG-2 may require Windows codec extensions.");
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                Ended?.Invoke();
            }
        });
    }

    private void OnVideoFrameAvailable(MediaPlayer sender, object args)
    {
        // While a render is queued, skip further events: the queued render copies the newest frame.
        if (_disposed || Volatile.Read(ref _failed) != 0 || Interlocked.Exchange(ref _framePending, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(RenderFrame, DispatcherPriority.Render);
    }

    private void RenderFrame()
    {
        Volatile.Write(ref _framePending, 0);
        if (_disposed || Volatile.Read(ref _failed) != 0)
        {
            return;
        }

        try
        {
            int width = checked((int)_player.PlaybackSession.NaturalVideoWidth);
            int height = checked((int)_player.PlaybackSession.NaturalVideoHeight);
            if (width == 0 || height == 0)
            {
                return;
            }

            if (_surface is null || _surface.Width != width || _surface.Height != height)
            {
                var previous = _surface;
                _surface = new SharedVideoSurface(width, height);
                SetBackBuffer();
                previous?.Dispose();
            }

            _image.Lock();
            try
            {
                _player.CopyFrameToVideoSurface(_surface.Surface);
                _surface.Flush();
                _image.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _image.Unlock();
            }

            FrameRendered?.Invoke();
        }
        catch (Exception exception) when (exception is COMException or SharpGenException
                                          or InvalidOperationException or ArgumentException)
        {
            ReportFailure($"Could not render the video frame: {exception.Message} " +
                          $"(0x{exception.HResult:X8})");
        }
    }

    // WPF drops the back buffer when its device is lost, so it must be set again once available.
    private void OnFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && _surface is not null && !_disposed)
        {
            SetBackBuffer();
        }
    }

    // Software fallback keeps video visible when WPF renders in software, such as over Remote Desktop.
    private void SetBackBuffer()
    {
        _image.Lock();
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface?.D3D9Surface ?? IntPtr.Zero,
            enableSoftwareFallback: true);
        _image.Unlock();
    }

    /// <summary>Reads one BGRA pixel of the last rendered frame. Used by tests.</summary>
    internal byte[] ReadPixel(int x, int y) =>
        (_surface ?? throw new InvalidOperationException("No frame has been rendered.")).ReadPixel(x, y);

    private void ReportFailure(string message)
    {
        if (_disposed || Interlocked.Exchange(ref _failed, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                Failed?.Invoke(message);
            }
        });
    }

    // Call on the dispatcher thread: rendering and the D3DImage both run there.
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _player.MediaOpened -= OnMediaOpened;
        _player.MediaFailed -= OnMediaFailed;
        _player.MediaEnded -= OnMediaEnded;
        _player.VideoFrameAvailable -= OnVideoFrameAvailable;
        _image.IsFrontBufferAvailableChanged -= OnFrontBufferAvailableChanged;
        _player.Pause();
        _player.Source = null;
        _player.Dispose();
        _source?.Dispose();
        if (_surface is not null)
        {
            var surface = _surface;
            _surface = null;
            SetBackBuffer();
            surface.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
