# Media

Put test video files in this folder. Subfolders are included.

- `VideoTestApp.exe -Stress` plays every video file here once, in alphabetical order by path, then exits.
- `VideoTestApp.exe -Stress -Loop` keeps playing them until you press Stop or close the app.

Supported extensions: `.mp4 .m4v .mov .mkv .webm .avi .wmv .asf .mpg .mpeg .m2v .ts .m2ts .vob .mxf`.
Other files, such as this README, are ignored.

The app reads videos from a `Media` folder beside `VideoTestApp.exe`. A build copies this folder there automatically. Publish does not, so for a published executable, create a `Media` folder beside it and add your videos.
