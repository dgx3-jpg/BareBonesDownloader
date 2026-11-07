using System;
using System.Globalization;
using System.IO;

namespace BareBonesDownloader
{
    internal static class MediaSizeLimiter
    {
        private const int AudioAttempts = 7;
        private const int VideoAttempts = 7;
        private const int MinimumAudioKbps = 16;
        private const int MinimumVideoKbps = 48;
        private const int MinimumVideoAudioKbps = 24;

        public static string ApplyAudio(string input, string toolsDirectory, AppSettings settings,
            ProcessJob job, Func<bool> shouldStop, Action<string> log)
        {
            if (settings == null || settings.AudioMaxSizeMb <= 0) return input;
            long maximumBytes = ToBytes(settings.AudioMaxSizeMb);
            if (new FileInfo(input).Length < maximumBytes) return input;

            double duration = ProbeDuration(input, toolsDirectory, job, shouldStop);
            AudioFormatOption selected = AudioFormats.Find(settings.AudioFormat);
            AudioFormatOption outputFormat = selected != null && selected.HasBitrate && !String.IsNullOrEmpty(selected.Encoder)
                ? selected : AudioFormats.Find("m4a");
            if (outputFormat == null || String.IsNullOrEmpty(outputFormat.Encoder))
                throw new InvalidOperationException("No supported audio encoder is available for the file size limit.");

            int bitrate = EstimateKbps(maximumBytes, duration, 0.88, MinimumAudioKbps, 512);
            log("Audio is over " + settings.AudioMaxSizeMb.ToString(CultureInfo.InvariantCulture) +
                " MB. Re-encoding to " + outputFormat.Label + " to meet the size limit.");
            string output = null;
            try
            {
                for (int attempt = 0; attempt < AudioAttempts; attempt++)
                {
                    if (shouldStop()) throw new OperationCanceledException();
                    output = NewOutputPath(input, outputFormat.Extension);
                    string[] args = new string[] {
                        "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", input,
                        "-map", "0:a:0", "-vn", "-map_metadata", "0", "-map_metadata:s:a:0", "0",
                        "-c:a", outputFormat.Encoder, "-b:a", bitrate.ToString(CultureInfo.InvariantCulture) + "k",
                        output
                    };
                    MusicMetadata.RunTool(Path.Combine(toolsDirectory, "ffmpeg.exe"), args, job, shouldStop);
                    if (shouldStop()) throw new OperationCanceledException();
                    if (File.Exists(output) && new FileInfo(output).Length < maximumBytes)
                    {
                        MusicMetadata.ValidateAudio(output, toolsDirectory, job, shouldStop);
                        if (shouldStop()) throw new OperationCanceledException();
                        string finalPath = Promote(input, output, outputFormat.Extension);
                        output = null;
                        return finalPath;
                    }
                    DeleteOutput(output);
                    output = null;
                    if (bitrate <= MinimumAudioKbps) break;
                    bitrate = LowerBitrate(bitrate, MinimumAudioKbps);
                }
                throw new IOException("The audio could not be encoded below the " +
                    settings.AudioMaxSizeMb.ToString(CultureInfo.InvariantCulture) +
                    " MB limit. The size cap may be too small for this track.");
            }
            finally { DeleteOutput(output); }
        }

        public static string ApplyVideo(string input, string toolsDirectory, int maximumSizeMb,
            ProcessJob job, Func<bool> shouldStop, Action<string> log)
        {
            if (maximumSizeMb <= 0) return input;
            long maximumBytes = ToBytes(maximumSizeMb);
            if (new FileInfo(input).Length < maximumBytes) return input;

            double duration = ProbeDuration(input, toolsDirectory, job, shouldStop);
            int totalKbps = EstimateKbps(maximumBytes, duration, 0.88,
                MinimumVideoKbps + MinimumVideoAudioKbps, 50000);
            log("Video is over " + maximumSizeMb.ToString(CultureInfo.InvariantCulture) +
                " MB. Re-encoding as MP4 to meet the size limit.");

            string output = null;
            try
            {
                for (int attempt = 0; attempt < VideoAttempts; attempt++)
                {
                    if (shouldStop()) throw new OperationCanceledException();
                    int audioKbps = Math.Min(96, Math.Max(MinimumVideoAudioKbps, totalKbps / 5));
                    int videoKbps = totalKbps - audioKbps;
                    if (videoKbps < MinimumVideoKbps)
                    {
                        videoKbps = MinimumVideoKbps;
                        audioKbps = MinimumVideoAudioKbps;
                        totalKbps = videoKbps + audioKbps;
                    }

                    output = NewOutputPath(input, "mp4");
                    string[] args = new string[] {
                        "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", input,
                        "-map", "0:v:0", "-map", "0:a:0", "-map_metadata", "0",
                        "-map_metadata:s:a:0", "0", "-map_chapters", "0", "-sn", "-dn",
                        "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
                        "-b:v", videoKbps.ToString(CultureInfo.InvariantCulture) + "k",
                        "-maxrate", videoKbps.ToString(CultureInfo.InvariantCulture) + "k",
                        "-bufsize", (videoKbps * 2).ToString(CultureInfo.InvariantCulture) + "k",
                        "-c:a", "aac", "-b:a", audioKbps.ToString(CultureInfo.InvariantCulture) + "k",
                        "-movflags", "+faststart", output
                    };
                    MusicMetadata.RunTool(Path.Combine(toolsDirectory, "ffmpeg.exe"), args, job, shouldStop);
                    if (shouldStop()) throw new OperationCanceledException();
                    if (File.Exists(output) && new FileInfo(output).Length < maximumBytes)
                    {
                        VideoOutput.Validate(output, Path.Combine(toolsDirectory, "ffprobe.exe"), job, shouldStop);
                        if (shouldStop()) throw new OperationCanceledException();
                        string finalPath = Promote(input, output, "mp4");
                        output = null;
                        return finalPath;
                    }
                    DeleteOutput(output);
                    output = null;
                    if (totalKbps <= MinimumVideoKbps + MinimumVideoAudioKbps) break;
                    totalKbps = LowerBitrate(totalKbps, MinimumVideoKbps + MinimumVideoAudioKbps);
                }
                throw new IOException("The video could not be encoded below the " +
                    maximumSizeMb.ToString(CultureInfo.InvariantCulture) +
                    " MB limit. The size cap may be too small for this video.");
            }
            finally { DeleteOutput(output); }
        }

        private static long ToBytes(int sizeMb)
        {
            return checked((long)sizeMb * 1000000L);
        }

        private static double ProbeDuration(string input, string toolsDirectory, ProcessJob job,
            Func<bool> shouldStop)
        {
            string value = MusicMetadata.RunTool(Path.Combine(toolsDirectory, "ffprobe.exe"), new string[] {
                "-v", "error", "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1", input
            }, job, shouldStop).Trim();
            double duration;
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out duration) ||
                Double.IsNaN(duration) || Double.IsInfinity(duration) || duration <= 0)
                throw new IOException("Could not determine media duration to apply the file size limit.");
            return duration;
        }

        private static int EstimateKbps(long maximumBytes, double duration, double safetyFactor,
            int minimum, int maximum)
        {
            double kbps = Math.Floor(maximumBytes * safetyFactor * 8.0 / duration / 1000.0);
            if (Double.IsNaN(kbps) || Double.IsInfinity(kbps) || kbps < minimum) return minimum;
            if (kbps > maximum) return maximum;
            return (int)kbps;
        }

        private static int LowerBitrate(int bitrate, int minimum)
        {
            if (bitrate <= minimum) return minimum;
            int next = (int)Math.Floor(bitrate * 0.8);
            return Math.Max(minimum, Math.Min(bitrate - 1, next));
        }

        private static string NewOutputPath(string input, string extension)
        {
            return Path.Combine(Path.GetDirectoryName(input),
                "bbd-size-" + Guid.NewGuid().ToString("N") + "." + extension);
        }

        private static string Promote(string input, string output, string extension)
        {
            string final = Path.Combine(Path.GetDirectoryName(input),
                Path.GetFileNameWithoutExtension(input) + "." + extension);
            if (File.Exists(final)) File.Replace(output, final, null);
            else File.Move(output, final);
            if (!String.Equals(input, final, StringComparison.OrdinalIgnoreCase) && File.Exists(input))
                File.Delete(input);
            return final;
        }

        private static void DeleteOutput(string output)
        {
            if (!String.IsNullOrEmpty(output) && File.Exists(output)) File.Delete(output);
        }
    }
}
