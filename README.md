# Video Test App

A Windows desktop test app for playing local video files through the Windows media pipeline, with a frame-rate overlay displayed at the top of the video.

Built with C# / WPF on .NET 10, using the Windows Media Foundation-backed `Windows.Media.Playback.MediaPlayer`.

## Features

- Play local video files with audio
- Open files from a dialog, by drag and drop, or by passing a path on the command line
- Play, pause, stop, seek, loop, and adjust volume
- Show an FPS overlay at the top of the video:
  - **Source FPS** – the nominal frame rate from the video track metadata. Variable-frame-rate files may report an average or nominal value.
  - **App-rendered FPS** – frames delivered to the app's display surface, measured over a rolling 2-second window
- Show codec subtype and resolution for the loaded video
- Show a clear error, including the HRESULT, when a file or codec cannot be played
- Show system information in the side panel:
  - Windows edition and version, matching `winver`
  - Hardware GPU name and driver version. Without a hardware GPU, such as on a Cloud PC, it shows the software render adapter and that decoding will use the CPU
  - Installed HEVC Video Extension package and version
- Run stress tests with `-Stress`, `-Loop`, or `-Loop <minutes>` using every video in the `Media` folder

> **Note:** App-rendered FPS counts frames the app copies into its display surface. Each decoded frame is copied GPU-to-GPU into a Direct3D texture that WPF shows through `D3DImage`, with no CPU copy. It is not a decoder-only benchmark and does not measure the frames your display actually shows. When WPF renders in software, such as over Remote Desktop, WPF copies frames through the CPU and the rate may be lower.

## Codec test list

A suggested set of codecs for building a `Media` folder. This is a practical test set, not a popularity ranking. A codec appearing here does not guarantee it will play. Playback depends on the Windows edition, installed decoders, container, profile, and bit depth.

| Codec | Typical containers | Windows support |
|---|---|---|
| H.264 / AVC | MP4, MOV, MKV, M2TS | Commonly built in |
| H.265 / HEVC | MP4, MOV, MKV | May need HEVC Video Extensions |
| AV1 | MP4, MKV, WebM | May need AV1 Video Extension |
| VP9 | WebM, MKV, MP4 | May need VP9 Video Extensions |
| VP8 | WebM, MKV | May need Web Media Extensions |
| MPEG-4 Part 2 | MP4, AVI | Profile and decoder dependent |
| MPEG-2 | MPG, MPEG, TS, VOB | May need MPEG-2 Video Extension |
| VC-1 | WMV, ASF, M2TS | Windows edition/profile dependent |
| Motion JPEG | AVI, MOV | Container and decoder dependent |
| Apple ProRes | MOV, MXF | Usually needs a compatible third-party decoder |

## Requirements

- Windows 10 version 2004 (build 19041) or later, x64 or ARM64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source
- Optional Windows codec extensions from the Microsoft Store for HEVC, AV1, VP9, and MPEG-2 content

## Build and run

The solution has two native platforms, **x64** and **ARM64**, and no Any CPU. In Visual Studio, choose the platform from the toolbar. On the command line, pass `-p:Platform`. Without it, the solution build defaults to ARM64.

```powershell
git clone https://github.com/jeffreyjean/VideoTestApp.git
cd VideoTestApp

dotnet build VideoTestApp.slnx -c Release -p:Platform=x64
dotnet build VideoTestApp.slnx -c Release -p:Platform=ARM64
dotnet run --project VideoTestApp\VideoTestApp.csproj -c Release -p:Platform=x64
```

Build output goes to `VideoTestApp\bin\<Platform>\Release\net10.0-windows10.0.19041.0\<win-x64|win-arm64>\`, with `Media` copied beside `VideoTestApp.exe`. An ARM64 build can be made on an x64 machine, but it runs only on an ARM64 device.

Open a video directly:

```powershell
dotnet run --project VideoTestApp\VideoTestApp.csproj -c Release -p:Platform=x64 -- "C:\path\to\video.mp4"
```

## Stress mode

The app reads videos from a `Media` folder beside `VideoTestApp.exe`. Subfolders are included, and non-video files are ignored. A build copies the repository's `Media` folder there automatically. Publish does not, so for a published executable create `publish\<runtime>\Media` yourself and add your videos.

| Arguments | Behavior |
|---|---|
| `-Stress` | Plays every video in `Media` once, in alphabetical order by path, then exits. |
| `-Loop` or `-Stress -Loop` | Plays every video repeatedly until you press **Stop** or close the app. `-Loop` automatically enables stress mode. |
| `-Loop 10` or `-Stress -Loop 10` | Repeats the videos for 10 minutes (600 seconds), then stops and exits, even mid-video. |

The optional `-Loop` duration is a positive number of minutes; fractional minutes such as `-Loop 0.5` are supported using a decimal point. The limit measures elapsed time from the start of the stress run, including opening videos and time spent paused. An invalid duration produces a warning and disables looping, so the app runs `-Stress` once instead of looping forever.

During a stress run:

- The app window stays open and shows the current pass and file number.
- **Pause** holds the current file. **Play** resumes it.
- **Stop** ends the stress run and leaves the app open.
- A file that fails to play, renders no frames, or doesn't open within 30 seconds is logged as a failure. The run then continues with the next file.
- The **Open video...** button, drag and drop, and the Loop checkbox are disabled.

### Exit code

| Code | Meaning |
|---|---|
| `0` | `-Stress` played every file and none failed. With `-Loop`, no file failed before the run was stopped or its time limit was reached; a video interrupted by the time limit is not marked as passed or failed. |
| `1` | A file failed, no video files were found, or a `-Stress` run was stopped before every file played. |

`VideoTestApp.exe` is a Windows GUI app, so PowerShell doesn't wait for it. To wait and read the exit code:

```powershell
$p = Start-Process .\publish\win-x64\VideoTestApp.exe -ArgumentList '-Stress' -Wait -PassThru
$p.ExitCode
```

### Stress log

Each run writes a live log to `%TEMP%\VideoTestApp\Stress_<timestamp>.log`, and the log path is shown in the status bar when the run ends. The log records:

- The file list
- Each file with `PASS` or `FAIL`
- Played duration, source FPS, and average app-rendered FPS
- Failure reasons
- Per-pass totals and a final summary

```text
2026-11-11 11:40:02.114  Pass 1, file 1/2: C:\...\Media\h264_1080p30.mp4
2026-11-11 11:40:12.530  PASS  h264_1080p30.mp4  played 10.2s  source 30 FPS  avg app-rendered 29.8 FPS
2026-11-11 11:40:12.561  Pass 1, file 2/2: C:\...\Media\av1_4k60.mkv
2026-11-11 11:40:13.020  FAIL  av1_4k60.mkv  Windows playback failed: UnsupportedMediaFormat ...
2026-11-11 11:40:13.051  Pass 1 complete: 1 passed, 1 failed.
```

## Test

```powershell
dotnet test VideoTestApp.slnx -c Release -p:Platform=x64
```

Use the platform that matches the machine: `x64` on an x64 PC and `ARM64` on an ARM64 device. Tests can't run under the other architecture.

The tests cover:

- FPS calculation over the rolling window, including reset and stall behavior
- H.264 integration playback with a generated clip: opening, source FPS metadata, frame rendering, pixel color checks, and seeking
- Invalid media reporting a failure
- Closing the main window before a video is loaded
- Command-line parsing, media discovery, and stress-run sequencing and logging
- End-to-end `-Stress` runs covering a passing file, a failing file, an empty `Media` folder, and `-Loop` continuing into a second pass

The integration tests use Windows media APIs and write temporary files under `C:\temp\VideoTestApp.Tests`.

## Publish

The app supports x64 and ARM64. Create a self-contained, single-file executable for each architecture:

```powershell
# x64
dotnet publish VideoTestApp\VideoTestApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\win-x64

# ARM64
dotnet publish VideoTestApp\VideoTestApp.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -o publish\win-arm64
```

Outputs: `publish\win-x64\VideoTestApp.exe` and `publish\win-arm64\VideoTestApp.exe`. Each is a self-contained single file that runs without .NET installed.

The latest published executables are checked in under `publish\` using Git LFS. Other files in `publish`, such as `.pdb` files, are ignored by git.

The `Media` folder is not copied on publish. To run a stress test with a published executable, create a `Media` folder beside `VideoTestApp.exe` and put your test videos in it:

```text
publish\win-x64\
  VideoTestApp.exe
  Media\
    your-test-video.mp4
```

## Project structure

```text
VideoTestApp.slnx       Solution with x64 and ARM64 platforms
Directory.Build.props   Maps each platform to its native Windows runtime
Media/                  Test videos for stress mode (copied next to the exe)
VideoTestApp/
  App.xaml(.cs)         App-wide styles and startup
  MainWindow.xaml(.cs)  UI, playback controls, FPS overlay, and stress mode
  VideoPlayback.cs      MediaPlayer frame-server playback into a D3DImage
  SharedVideoSurface.cs D3D11 texture shared with D3D9Ex for zero-copy display
  FrameRateMeter.cs     Rolling-window FPS measurement
  StartupOptions.cs     Command-line parsing (-Stress, -Loop, file path)
  MediaLibrary.cs       Media folder discovery
  StressRun.cs          Stress sequencing, results, and live log
  SystemInfo.cs         Windows version, GPU adapters and driver versions, HEVC package
VideoTestApp.Tests/
  FrameRateMeterTests.cs
  StressTests.cs
  VideoPlaybackTests.cs
```

## Version

0.1.0 – initial version
