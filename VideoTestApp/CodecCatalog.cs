namespace VideoTestApp;

public sealed record CodecEntry(string Name, string Containers, string WindowsSupport);

public static class CodecCatalog
{
    public static IReadOnlyList<CodecEntry> Entries { get; } =
    [
        new("H.264 / AVC", "MP4, MOV, MKV, M2TS", "Commonly built in"),
        new("H.265 / HEVC", "MP4, MOV, MKV", "May need HEVC Video Extensions"),
        new("AV1", "MP4, MKV, WebM", "May need AV1 Video Extension"),
        new("VP9", "WebM, MKV, MP4", "May need VP9 Video Extensions"),
        new("VP8", "WebM, MKV", "May need Web Media Extensions"),
        new("MPEG-4 Part 2", "MP4, AVI", "Profile and decoder dependent"),
        new("MPEG-2", "MPG, MPEG, TS, VOB", "May need MPEG-2 Video Extension"),
        new("VC-1", "WMV, ASF, M2TS", "Windows edition/profile dependent"),
        new("Motion JPEG", "AVI, MOV", "Container and decoder dependent"),
        new("Apple ProRes", "MOV, MXF", "Usually needs a compatible third-party decoder")
    ];
}
