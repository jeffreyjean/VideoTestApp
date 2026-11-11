namespace VideoTestApp;

public sealed class FrameRateMeter
{
    private readonly Queue<TimeSpan> _frames = new();
    private readonly TimeSpan _window;
    private TimeSpan _startedAt;

    public FrameRateMeter(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        _window = window;
    }

    public void Reset(TimeSpan now)
    {
        _frames.Clear();
        _startedAt = now;
    }

    public void RecordFrame(TimeSpan now)
    {
        _frames.Enqueue(now);
        Trim(now);
    }

    public double GetFramesPerSecond(TimeSpan now)
    {
        Trim(now);
        var elapsed = now - _startedAt;
        var seconds = Math.Min(elapsed.TotalSeconds, _window.TotalSeconds);
        return seconds > 0 ? _frames.Count / seconds : 0;
    }

    private void Trim(TimeSpan now)
    {
        var cutoff = now - _window;
        while (_frames.TryPeek(out var frame) && frame <= cutoff)
        {
            _frames.Dequeue();
        }
    }
}
