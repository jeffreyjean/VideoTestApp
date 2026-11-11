using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
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
    private readonly SemaphoreSlim _frameGate = new(1, 1);
    private MediaSource? _source;
    private MediaPlaybackItem? _item;
    private VideoFrame? _surface;
    private byte[]? _pixels;
    private WriteableBitmap? _bitmap;
    private volatile bool _disposed;
    private int _failed;

    public event Action<VideoInfo>? Opened;
    public event Action<WriteableBitmap>? FrameRendered;
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
    }

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

    private async void OnVideoFrameAvailable(MediaPlayer sender, object args)
    {
        if (_disposed || Volatile.Read(ref _failed) != 0 || !_frameGate.Wait(0))
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }

            int width = checked((int)sender.PlaybackSession.NaturalVideoWidth);
            int height = checked((int)sender.PlaybackSession.NaturalVideoHeight);
            if (width == 0 || height == 0)
            {
                return;
            }

            if (_surface is null || _surface.Direct3DSurface.Description.Width != width ||
                _surface.Direct3DSurface.Description.Height != height)
            {
                _surface?.Dispose();
                _surface = VideoFrame.CreateAsDirect3D11SurfaceBacked(
                    DirectXPixelFormat.B8G8R8A8UIntNormalized, width, height);
                _pixels = new byte[checked(width * height * 4)];
            }

            sender.CopyFrameToVideoSurface(_surface.Direct3DSurface);
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                _surface.Direct3DSurface, BitmapAlphaMode.Ignore);
            bitmap.CopyToBuffer(_pixels!.AsBuffer());

            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed || Volatile.Read(ref _failed) != 0)
                {
                    return;
                }

                if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                {
                    _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                }

                _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width * 4, 0);
                FrameRendered?.Invoke(_bitmap);
            }, DispatcherPriority.Render);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
                                          or ArgumentException or IOException)
        {
            if (!_disposed)
            {
                ReportFailure($"Could not render the video frame: {exception.Message} " +
                              $"(0x{exception.HResult:X8})");
            }
        }
        finally
        {
            _frameGate.Release();
        }
    }

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.MediaOpened -= OnMediaOpened;
        _player.MediaFailed -= OnMediaFailed;
        _player.MediaEnded -= OnMediaEnded;
        _player.VideoFrameAvailable -= OnVideoFrameAvailable;
        _player.Pause();
        // Drain an in-flight GPU readback before releasing its player and surface.
        await _frameGate.WaitAsync();
        try
        {
            _player.Source = null;
            _surface?.Dispose();
            _player.Dispose();
            _source?.Dispose();
        }
        finally
        {
            _frameGate.Release();
        }
    }
}
