# Bare Bones Downloader

A small Windows desktop app that queues YouTube links and saves audio or video with sound in folders you choose.

## Run it

1. Extract the ZIP to a folder you can write to.
2. Run setup.cmd once and wait for the tool checks to finish.
3. Open BareBonesDownloader.exe and choose **Audio only** or **Video + audio** at the top of the window.
4. Choose a destination folder for each type in Settings. Audio can also be stored in the selected Video folder and vice versa if you choose the same location.
5. Paste one or more YouTube video links, then select **Queue audio** or **Queue video**. Both types run in the same queue. The selected type applies to every link currently in the paste box.

Setup needs an internet connection and downloads yt-dlp, FFmpeg, ffprobe, Deno, and rsgain. Close the app before running setup again. Rerun setup when YouTube changes stop downloads from working.

## Settings

Settings are saved in the current Windows user's Local AppData. The app keeps separate Audio and Video folders. General settings can append a Windows-safe YouTube URL suffix to audio and video filenames. This is off by default; when enabled, URL separators become underscores and the title is shortened slightly to leave room. A queued download keeps its type, folder, and audio options from the time it was added.

Settings → Audio controls output format and encoding quality, and can optionally cap each audio file's size. Settings → Video controls maximum resolution and can optionally cap each video file's size. Both size limits are independent and off by default. Sizes use decimal MB, so 8 MB means 8,000,000 bytes. Settings → Appearance offers Light and Dark themes. Light is the default. Windows High Contrast uses system colours.

## Video files

Video downloads are MP4 files with H.264 video and AAC audio. Settings → Video can cap the source resolution at Best available, 2160p, 1440p, 1080p, 720p, 480p or 360p. If YouTube only provides another codec at a higher resolution, the saved quality follows its compatible H.264 streams. An optional maximum file size re-encodes an oversized video at a lower bitrate and checks the finished file is below the cap before publishing. Files already under the cap are left untouched. Very small caps may be impossible to meet; in that case the item fails and is not published. Video downloads skip music tags and ReplayGain settings.

The app verifies the finished MP4 has both the expected video and audio streams before publishing it. Video files include the YouTube ID in their filename. Existing files are left unchanged.

## App icon

`assets/penrose-white-grey.svg` is the supplied icon source. `assets/BareBonesDownloader.ico` contains transparent frames at 16, 24, 32, 48, 64, 128, and 256 pixels. To rebuild the ICO, install Pillow for Python 3 and use Microsoft Edge.

Run this PowerShell command from the source folder:

```powershell
$svg = (Resolve-Path 'assets/penrose-white-grey.svg').Path
$png = Join-Path $env:TEMP 'BareBonesDownloader-icon.png'
$edge = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
$url = ([Uri]$svg).AbsoluteUri
& $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 '--window-size=256,256' ('--screenshot=' + $png) $url
python -c "from PIL import Image; Image.open(r'$png').save('assets/BareBonesDownloader.ico', format='ICO', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])"
Remove-Item -LiteralPath $png
```

## Audio files

Settings → Audio offers Best available, AAC, ALAC, FLAC, M4A, MP3, Opus, Vorbis, and WAV. Best available preserves the source audio where possible. Other formats are encoded only when needed. Default quality permits stream copying; explicit variable quality or bitrate re-encodes the chosen codec even when the source already uses it. Quality is limited by the source audio, and converting compressed audio does not restore lost quality. An optional maximum file size leaves files under the cap unchanged and re-encodes larger files at a lower bitrate. If re-encoding is needed for Best available or a lossless format, the capped output uses AAC in M4A. Very small caps may be impossible to meet; in that case the item fails and is not published.

New filenames use the video title without a video ID suffix. Downloads are extracted and tagged in the app's work/ folder first. Only the completed audio file is copied into the chosen folder. An existing file with the same name is left unchanged, even if a music player has it open. Completed working files are removed; failed or cancelled downloads keep their working files for a retry.

The app transports completed filenames as JSON and forces UTF-8 output so Japanese and other Unicode names survive Windows console encoding.

## Tags and loudness

The app uses YouTube's structured music metadata first, followed by the auto-generated Topic description and any tags already in the audio. If title, artist or album is still missing, it searches Apple's online music catalogue, starting with the Japanese or UK catalogue as appropriate. It compares the song title, artist and duration and fills only missing fields from a confident match. Settings → Metadata controls which supported fields are written; unchecked source tags are removed. The local sample preview updates with the selected fields.

Tag-writing and verification errors are logged as metadata warnings. A completed file with a readable audio stream is still saved to the destination.

Unavailable searches and ambiguous matches do not stop the download. Remaining unknown fields are left empty and explained in the log. Lookups have a short timeout, observe the catalogue request limit, and can be stopped with Cancel current. No extra setup or API key is required.

Windows File Explorer may leave Opus Details columns blank even when the file contains tags. Opus-compatible music players can read the embedded tags.

Settings → Audio controls whether rsgain scans each supported output after encoding and writes per-track ReplayGain 2.0 loudness and peak tags. The scan does not re-encode or change the audio. ReplayGain-compatible players use the tags during playback; the app does not apply album gain because each link is processed as an individual track. Metadata and ReplayGain failures are warnings; readable audio is still saved.

## What the queue accepts

- HTTP or HTTPS links to one YouTube video, including watch, Shorts, and youtu.be links.
- A video link that also has playlist parameters. The app removes those parameters and downloads that video only.

Channel links, playlist-only links, malformed links, and live streams are not supported. The app does not sign in, use browser cookies, or record live streams. Queue and log data are kept only for the current session. The selected folder path is saved in the current Windows user's Local AppData settings.

Use the downloader only for material you have permission to save and in line with applicable terms.

## Troubleshooting

- If the app says setup is required, close it and run setup.cmd again.
- If the selected folder disappears or becomes unwritable, the queue pauses and asks you to choose another existing folder.
- A Failed item does not stop the items after it. Read the log for yt-dlp's error. The window stays open after success or failure until you close it.
- Cancel current stops yt-dlp, FFmpeg, or rsgain and their child processes. Partial files are retained so yt-dlp may resume them later.
- Closing with unfinished work asks whether to keep the window open; confirming closes the downloader and loses waiting queue entries.

## Build

Run build.ps1 to rebuild BareBonesDownloader.exe from the C# 5-compatible source in src/. The build uses the .NET Framework 4.x compiler already present on Windows; no Python, SDK, administrator rights, or PATH changes are required.

Downloaded binaries are kept in tools/ and are not included in source control. setup.cmd downloads official releases over HTTPS, validates published SHA-256 checksums where available, checks each tool's version command, and stages updates before replacing existing tools. Third-party license and notice files are stored in tools/licenses/.

## Upstream references

- yt-dlp installation, dependencies, and command options: https://github.com/yt-dlp/yt-dlp
- YouTube JavaScript runtime and EJS setup: https://github.com/yt-dlp/yt-dlp/wiki/EJS
- FFmpeg builds for yt-dlp: https://github.com/yt-dlp/FFmpeg-Builds
- Official Deno releases: https://github.com/denoland/deno/releases
- rsgain ReplayGain 2.0 tagging utility: https://github.com/complexlogic/rsgain
