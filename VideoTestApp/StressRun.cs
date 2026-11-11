using System.IO;

namespace VideoTestApp;

public sealed class StressRun : IDisposable
{
    private readonly IReadOnlyList<string> _files;
    private readonly StreamWriter _log;
    private int _index = -1;
    private int _passPassed;
    private int _passFailed;

    public StressRun(IReadOnlyList<string> files, bool loop, string logPath)
    {
        _files = files;
        Loop = loop;
        LogPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        Log($"Stress run started. Files: {files.Count}. Loop: {loop}.");
        foreach (var file in files)
        {
            Log($"  {file}");
        }
    }

    public bool Loop { get; }
    public string LogPath { get; }
    public int Pass { get; private set; } = 1;
    public int FileNumber => Math.Min(_index + 1, _files.Count);
    public int FileCount => _files.Count;
    public string? CurrentFile => _index >= 0 && _index < _files.Count ? _files[_index] : null;
    public int TotalPassed { get; private set; }
    public int TotalFailed { get; private set; }
    public bool Completed { get; private set; }

    public static string CreateLogPath() => Path.Combine(
        Path.GetTempPath(), "VideoTestApp", $"Stress_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");

    /// <summary>Returns the next file, or null when every file has played once and Loop is off.</summary>
    public string? MoveNext()
    {
        if (Completed || _files.Count == 0)
        {
            return null;
        }

        if (++_index >= _files.Count)
        {
            Log($"Pass {Pass} complete: {_passPassed} passed, {_passFailed} failed.");
            if (!Loop)
            {
                Completed = true;
                return null;
            }

            Pass++;
            _index = 0;
            _passPassed = 0;
            _passFailed = 0;
        }

        Log($"Pass {Pass}, file {FileNumber}/{FileCount}: {CurrentFile}");
        return CurrentFile;
    }

    public void RecordPass(TimeSpan played, double? sourceFps, double renderedFps)
    {
        TotalPassed++;
        _passPassed++;
        var source = sourceFps is double fps ? fps.ToString("0.###") : "--";
        Log($"PASS  {Path.GetFileName(CurrentFile)}  played {played.TotalSeconds:0.0}s  " +
            $"source {source} FPS  avg app-rendered {renderedFps:0.0} FPS");
    }

    public void RecordFailure(string reason)
    {
        TotalFailed++;
        _passFailed++;
        Log($"FAIL  {Path.GetFileName(CurrentFile)}  {reason}");
    }

    public void Log(string message) => _log.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}");

    public void Finish(string reason) =>
        Log($"Stress run ended: {reason} Passes started: {Pass}. Total passed: {TotalPassed}. " +
            $"Total failed: {TotalFailed}.");

    public void Dispose() => _log.Dispose();
}
