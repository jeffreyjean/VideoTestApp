using System.IO;

namespace VideoTestApp;

public static class MediaLibrary
{
    public static IReadOnlyList<string> VideoExtensions { get; } =
    [
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".asf",
        ".mpg", ".mpeg", ".m2v", ".ts", ".m2ts", ".vob", ".mxf"
    ];

    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "Media");

    public static IReadOnlyList<string> FindVideos(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => VideoExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
