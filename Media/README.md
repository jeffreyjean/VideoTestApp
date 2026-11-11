# Media

Put test video files in this folder. Subfolders are included.

- `VideoTestApp.exe -Stress` plays every video file here once, in alphabetical order by path, then exits.
- `VideoTestApp.exe -Stress -Loop` keeps playing them until you press Stop or close the app.

Supported extensions: `.mp4 .m4v .mov .mkv .webm .avi .wmv .asf .mpg .mpeg .m2v .ts .m2ts .vob .mxf`.
Other files, such as this README, are ignored.

This folder is copied next to `VideoTestApp.exe` on build and publish.
