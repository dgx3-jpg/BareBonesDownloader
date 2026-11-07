# BareBonesDownloader video and appearance implementation plan

**Goal** Update the existing Windows app so the main window can queue audio-only files or videos with sound, configure video resolution, optionally limit audio and video output sizes, and add Light and Dark appearance settings plus the supplied Penrose app icon.

**Architecture** Keep the WinForms app and its single FIFO worker. Capture the download type and settings for each batch, branch only where the audio and video pipelines differ, and reuse process cancellation, final-path reporting and safe publication.

**Baseline** Work in `E:\Daniel\Documents\GitHub\BareBonesDownloader`. This design was checked against `73ac895`, whose source matches the deployed vault copy after line-ending normalization. Build on the current code, including the audio format, music tag and ReplayGain options. Keep C# 5, .NET Framework 4.8, Windows 10/11, `build.ps1`, and the existing local tools. The design and ordered tasks below are the implementation brief.

## 1. Main window and download workflow

Rename the window to **Bare Bones Downloader**. Put **Download as** at the top, immediately above the destination and link input. Use a grouped pair of large, equal-width native radio buttons. Give the selected option a visible checked indicator, border or fill, and keyboard focus styling. **Audio only** is selected at each launch.

```text
Bare Bones Downloader                                      [Settings…]

Download as       (●) Audio only         ( ) Video + audio
                  Audio format and size from Settings.
Audio folder      [ E:\Music                              ] [Change…]

YouTube links
[ Paste one or more links                                             ]
[                                                                     ]
[ Queue audio ]  [ Cancel current ]  [ Clear waiting ]

YouTube link                                      Type              Status
https://www.youtube.com/watch?v=…                 Audio only        Downloading
https://www.youtube.com/watch?v=…                 Video + audio     Waiting

Log
```

| When selected | Destination row | Helper text | Primary action |
| --- | --- | --- | --- |
| Audio only | Audio folder | Audio format and size from Settings. | Queue audio |
| Video + audio | Video folder | MP4 with sound · Video quality and size from Settings. | Queue video |

- Clicking the primary action applies that type to every accepted link in the paste box and starts work if the queue is idle. Keep the existing link parser, whitespace input, and visible rejection behavior. Give rejected rows the attempted type and keep rejected text available for correction.
- Switching type updates the helper text, folder row and primary action immediately. Keep the pasted links and existing queue rows intact. Keep the switch available during downloads so users can add either type to the same FIFO.
- Put **Type** in the queue and show **Audio only** or **Video + audio** on every row. Keep the existing statuses. A video remains **Downloading** through merge, validation and publication. Use the log for detailed failures and the actual output path.
- The **Change…** button edits the currently selected type's folder. If **Queue audio** or **Queue video** needs a folder, open the picker for that type and continue the same submission only when it saves successfully. Preserve the draft on Cancel. A fresh install can configure either type first. Opening the app or switching type should not open a folder picker by itself.
- Tab to the radio group, select with arrow keys, and then tab through the folder button, links, and queue action. Keep status and type readable without colour and keep the type and status columns visible at the minimum window size.

## 2. Queue identity and destination settings

- Add `DownloadKind` with `Audio` and `Video`. Capture the type and a copy of the current settings once for the batch. Put the effective destination on each queue item. Worker code reads the item, including its audio format and size limit or video resolution and size limit, without consulting the current radio selection.
- Reserve an ordinal key made from the YouTube video ID and kind. One pending or active job of each kind is allowed for the same ID. Reject same-kind duplicates with a log entry naming the type. Release reservations on completion, failure, cancellation, Clear waiting, and shutdown. Keep one worker, the existing FIFO order, and the existing close confirmation.
- Keep the JSON key `destination` for audio. Add optional `videoDestination`, unset by default, plus `videoMaxHeight`, `audioMaxSizeMb`, `videoMaxSizeMb` and `appendYouTubeUrlToFilename`. Zero means no height ceiling or no size limit; the filename URL option defaults off. Preserve existing settings and atomic save behavior under `%LOCALAPPDATA%\BareBonesDownloader\settings.json`. Settings → General shows **Audio folder** and **Video folder** on two fixed-height rows so each label aligns with its path and picker, plus a checkbox to append the canonical YouTube URL to audio and video filenames. Replace Windows-invalid URL separators with underscores and shorten the title segment to 110 bytes to leave room. The user can explicitly choose the same folder for both. Seed the pickers with the saved path if it exists, otherwise Windows Music or Videos as appropriate. Store only a user-confirmed existing writable path.
- Normal folder edits affect future jobs. Validate changed paths when saving Settings and allow an Appearance-only save even with an unset or unavailable folder. Check the selected mode's folder on enqueue and each item's captured destination again before starting.
- If a captured path becomes unavailable, hold that item at the front and keep the FIFO paused. Replace the existing auto-retry through Settings with a visible **Choose replacement…** action on a pause message naming the blocked type and path. The picker targets the blocked item regardless of the selected radio option. Explain that confirmation updates the saved folder and waiting jobs of that type that reference that path. After a successful save, update those items under the queue lock, preserve their order, and wake the worker. A cancelled picker, failed save, or unrelated Settings edit leaves the pause in place. Clear waiting clears the pause and releases those reservations. Choosing the original path again works once it is writable.

## 3. Video output and validation

Save one `.mp4` containing H.264 video and AAC audio. Settings → Video offers a maximum resolution of Best available, 2160p, 1440p, 1080p, 720p, 480p or 360p. Prefer the best compatible separate streams at or below that ceiling, and use a compatible combined stream when the separate pair is unavailable. Quality depends on the H.264 streams YouTube provides. Keep codec and container fixed to preserve predictable MP4 output.

Build the video branch from the existing yt-dlp common arguments. Keep absolute tools, Windows argument quoting, UTF-8, live filtering, `--no-playlist`, Windows-safe filenames, `--no-force-overwrites`, and the existing final-path marker. Pass the following video arguments as individual values before `--` and the canonical URL.

```text
--format
bv[ext=mp4][vcodec^=avc1][height<=<maximum>]+ba[ext=m4a][acodec^=mp4a]/b[ext=mp4][vcodec^=avc1][acodec^=mp4a][height<=<maximum>]
--merge-output-format
mp4
--output
%(title).150B [%(id)s].%(ext)s
```

With **Best available**, FFmpeg muxes a selected pair without re-encoding. For a resolution ceiling, replace `<maximum>` in the example with the selected height in pixels; omit the height filter for Best available. Append the filter to both split-video and combined fallback formats. If no matching format is available, fail the item with the yt-dlp diagnostic and a message that Video requires a compatible MP4 with sound at the selected resolution. Keep network, launch and live-filter errors distinguishable. [yt-dlp format selection](https://github.com/yt-dlp/yt-dlp#format-selection), [yt-dlp video options](https://github.com/yt-dlp/yt-dlp#video-format-options)

Settings → Audio and Settings → Video each have an independent optional maximum file size, entered in decimal MB (1 MB = 1,000,000 bytes). A disabled limit is stored as zero. Compare the finished staged file to the strict byte limit before publishing. If it is already smaller, leave it untouched. Otherwise use ffprobe duration and FFmpeg bitrate-controlled re-encoding, retaining metadata where supported. For capped audio, keep a selected bitrate-capable lossy codec; Best available and lossless formats use AAC-in-M4A when re-encoding is necessary. For capped video, encode H.264/AAC MP4. Use a conservative bitrate budget, then verify actual bytes and lower bitrates/retry until the file is strictly below the limit. If the minimum usable bitrate still exceeds the limit, fail without publishing. Run these encodes through the existing cancellable process job and preserve resumable source files on failure or cancellation. Explain quality loss and the possibility of an impossible cap in both Settings tabs.

- Keep the current audio arguments, title-only audio filenames and audio processing. Video skips extraction, custom audio encoding, music tag processing, catalogue lookup and ReplayGain. Omit the audio branch's info JSON and metadata flags for Video.
- Keep the ReplayGain option and its support status inside Settings → Audio, alongside format, quality and size controls. Remove the separate ReplayGain tab.
- Keep audio's existing `work/<id>` and `work/<id>.lock`. Video uses `work/video-<key>` and `work/video-<key>.lock` with the same ID validation and exclusive lock. Use the lowercase hex of the 11 ASCII ID bytes as the key so IDs that differ only in case stay separate on Windows. Resume same-type partials where yt-dlp supports it. A successful run or existing-file skip cleans only its own work directory. Leave failed or cancelled staging files available for retry.
- After a successful yt-dlp exit, resolve the existing `BBD_FILE:` JSON record from `--print after_move:BBD_FILE:%(filepath)j`. For Video require a real `.mp4` inside that job's staging directory, with no reparse-point escape. Ignore unmarked paths in diagnostics and paths outside that job. Fail if the file is missing or the probe fails.
- Run the existing bundled ffprobe through the cancellable job. Use `-v error -show_entries stream=codec_type,codec_name:stream_disposition=attached_pic -of json <path>` and require an H.264 video stream with `attached_pic=0` and an AAC audio stream. Treat a cover image, video-only file, or audio-only file as a failed output. [FFprobe stream selection and JSON](https://ffmpeg.org/ffprobe.html)
- Generalize `DownloadFiles.PublishAudio` to `PublishCompletedFile` and keep its temporary sibling copy and no-overwrite move. Publish only after validation. On a same-name collision, leave the destination file untouched and log the skip with the path. Cancel or close stops the entire process tree, including merge and probe, before the worker starts anything else. Cancellation before the final move removes the destination copy in progress. Once the move succeeds, record the item as finished.

## 4. Appearance and app icon

- Add **Appearance** to Settings with a **Theme** choice of **Light** and **Dark**. Persist optional JSON `theme` as `light` or `dark`. Use Light for missing or unknown values. Apply the stored theme before showing the main window and apply changes to the current window immediately after a successful Save. Cancel and save failure leave the current theme active. Reopen Settings in the active theme.
- Put the shared palette in `src/AppTheme.cs`. Light uses the current Windows control styling. Dark uses `#070F2B` for the form, `#1B1A55` for input and list surfaces, `#F7F6FF` for primary text, `#9290C3` for muted text, borders and selection, and `#535C91` for button hover. Use dark text on a periwinkle selection. Cover the input, read-only path, log, list headers and row selection, radio options, buttons and disabled states, plus all Settings tabs and controls. Use built-in owner drawing where native controls ignore palette colours. The Windows title bar, scrollbars, message boxes and folder picker may follow system styling. In Windows High Contrast use system colours and native focus/selection states. Reapply on a system preference change and unsubscribe when the form is disposed. [WinForms owner drawing](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/controls-with-built-in-owner-drawing-support), [WinForms High Contrast guidance](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/walkthrough-creating-an-accessible-windows-based-application)
- Copy the supplied SVG to `assets/penrose-white-grey.svg` unchanged. Use its exact grey, white and cyan artwork in both themes. Generate `assets/BareBonesDownloader.ico` once from the SVG with 32-bit alpha frames at 16, 24, 32, 48, 64, 128 and 256 px. Preserve transparent corners. Keep the SVG and ICO in source and any source-inclusive ZIP. Record the conversion tool and command in the README so the ICO can be regenerated.
- Pass `/win32icon:assets\BareBonesDownloader.ico` and `/resource:assets\BareBonesDownloader.ico,BareBonesDownloader.AppIcon.ico` to `csc` in `build.ps1`. Load the embedded managed icon for the main and Settings forms, with its lifetime managed by the app. The EXE must show the icon when moved away from the asset folder. Keep the existing build and setup flow and use the checked-in ICO during build. [C# compiler resource options](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/win32icon-compiler-option), [Windows icon format](https://learn.microsoft.com/en-us/windows/win32/menurc/about-icons)

## Implementation order and acceptance checks

Run tests from a disposable staged copy with temporary destinations and isolated settings. Use the same Framework compiler and references as `build.ps1`. Compile each test harness with `src/*.cs` and `/main:BareBonesDownloader.<TestClass>` where it uses app internals. Build `tests/FakeDownloader.cs` separately and place copies under the tool names used by `tests/DownloaderRegressionTests.cs`.

- [ ] **Queue and folder model.** Update `src/SettingsStore.cs`, `src/SettingsForm.cs` and `src/DownloaderForm.cs`. Verify that a legacy audio settings file preserves its folder, codec, tags and ReplayGain. Using the fake downloader, enqueue Audio A, Video A and Video B while Audio A is active. Confirm FIFO order, same-kind duplicate rejection, and reservation release on clear, cancellation and failure. Change the selected type and both folders after enqueue and confirm that those jobs keep their snapshots. Remove a queued destination, then confirm that cancelling recovery leaves it paused and that a replacement changes only jobs matching the blocked type and path.
- [ ] **Video quality and size limits.** Check each selected yt-dlp height ceiling and MP4 fallback. Confirm a valid existing file below the cap is not re-encoded; oversized audio/video is bitrate-reduced and the final staged bytes are strictly below the decimal-MB cap; impossible caps fail without publishing; the queue continues afterward; and cancellation during FFmpeg leaves no published partial. Confirm audio tags survive size-limited encoding and the video remains H.264/AAC. Check that an unset cap retains existing source quality and codec behavior.
- [ ] **Main window.** Wire the mode controls and context copy in `src/DownloaderForm.cs`. Test keyboard-only queueing, switching with pasted links, first-run Video without an audio folder, a cancelled first-run picker, mixed valid and invalid links, and a mixed queue at minimum size and 125% and 150% scaling.
- [ ] **Theme and icon.** Add `src/AppTheme.cs`, theme persistence and Appearance controls. Integrate the asset files and compiler resources. Build with `.\build.ps1`. Exercise Light → Dark → Light, restart, Cancel, invalid theme JSON, saving while a download is active and saving with no folder configured. Inspect both windows in Dark and High Contrast, including disabled text, keyboard focus, selected rows and tabs. Check the copied EXE's icon in Explorer, the main window, Alt+Tab and the taskbar at standard and scaled DPI.
- [ ] **Delivery.** Update `README.md` with the mode selector, folders, video quality behavior, appearance options and icon conversion command. If producing the usual ZIP, include the icon sources with the existing build files and run the app from an extracted copy. With permission to save the test material, download one short source as both types and verify the MP4 plays with picture and sound, ffprobe reports H.264 and AAC, audio matches the chosen audio settings, and only the completed outputs reach their respective folders. Record what was actually tested.

## Supplied SVG

Use `C:\Users\Daniel\Downloads\penrose-white-grey.svg` if present. Its SHA-256 is `BAE3FE3ADCA9D9E2C961E902360A8F6808E83CB7A799B623BFF6376383F52957`. The SVG source is included below so the handoff survives a Downloads cleanup.

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" role="img">
  <title>White and grey Penrose bone icon</title>
  <rect width="256" height="256" rx="56" fill="#111315"/>
  <g transform="translate(128 140)" stroke="#111315" stroke-width="1.35" stroke-linejoin="round">
    <g transform="rotate(0)">
      <path d="M 0 -77 L 23 -77 L 89.684 38.5 L 66.684 38.5 L -43.684 38.5 L -32.184 18.581 L 55.184 18.581 Z" fill="#ffffff"/>
      <path d="M -11.5 -96.919 C -25.24 -97.919 -26.733 -116.919 -13.434 -116.919 Q -4.749 -116.919 0 -109.719 Q 4.749 -116.919 13.434 -116.919 C 26.733 -116.919 25.24 -97.919 11.5 -96.919 L 23 -77 L 0 -77 Z" fill="#27deea"/>
    </g>
    <g transform="rotate(120)">
      <path d="M 0 -77 L 23 -77 L 89.684 38.5 L 66.684 38.5 L -43.684 38.5 L -32.184 18.581 L 55.184 18.581 Z" fill="#c7c7c7"/>
      <path d="M -11.5 -96.919 C -25.24 -97.919 -26.733 -116.919 -13.434 -116.919 Q -4.749 -116.919 0 -109.719 Q 4.749 -116.919 13.434 -116.919 C 26.733 -116.919 25.24 -97.919 11.5 -96.919 L 23 -77 L 0 -77 Z" fill="#20c9d5"/>
    </g>
    <g transform="rotate(240)">
      <path d="M 0 -77 L 23 -77 L 89.684 38.5 L 66.684 38.5 L -43.684 38.5 L -32.184 18.581 L 55.184 18.581 Z" fill="#8e8e8e"/>
      <path d="M -11.5 -96.919 C -25.24 -97.919 -26.733 -116.919 -13.434 -116.919 Q -4.749 -116.919 0 -109.719 Q 4.749 -116.919 13.434 -116.919 C 26.733 -116.919 25.24 -97.919 11.5 -96.919 L 23 -77 L 0 -77 Z" fill="#12aeba"/>
    </g>
  </g>
</svg>
```
