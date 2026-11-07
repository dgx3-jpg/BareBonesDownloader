using System;
using System.Collections.Generic;
using System.IO;

namespace BareBonesDownloader
{
    internal static class AudioEncoding
    {
        public static string Apply(string input, string toolsDirectory, AppSettings settings,
            ProcessJob job, Func<bool> shouldStop)
        {
            if (settings == null || settings.AudioFormat == "best") return input;
            settings.Normalize();
            AudioFormatOption format = AudioFormats.Find(settings.AudioFormat);
            if (format == null) throw new InvalidOperationException("The selected audio format is not supported.");

            string output = Path.Combine(Path.GetDirectoryName(input),
                Path.GetFileNameWithoutExtension(input) + ".encoded" + AudioFormats.ExtensionFor(format.Id, input));
            bool explicitQuality = settings.QualityMode == "variable" || settings.QualityMode == "bitrate";
            string inputCodec = ProbeCodec(input, toolsDirectory, job, shouldStop);
            bool copy = !explicitQuality && IsCopyCompatible(format, inputCodec);
            List<string> args = new List<string> {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", input,
                "-map", "0:a:0", "-vn", "-map_metadata", "0", "-map_metadata:s:a:0", "0",
                "-c:a", copy ? "copy" : format.Encoder
            };
            if (!copy) AddQualityArguments(args, format, settings);
            args.Add(output);
            try
            {
                MusicMetadata.RunTool(Path.Combine(toolsDirectory, "ffmpeg.exe"), args.ToArray(), job, shouldStop);
                if (shouldStop()) throw new OperationCanceledException();
                File.Delete(input);
                string final = Path.Combine(Path.GetDirectoryName(input), Path.GetFileNameWithoutExtension(input) + AudioFormats.ExtensionFor(format.Id, input));
                if (!String.Equals(output, final, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(final)) File.Delete(final);
                    File.Move(output, final);
                }
                return final;
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }

        private static string ProbeCodec(string input, string toolsDirectory, ProcessJob job, Func<bool> shouldStop)
        {
            string json = MusicMetadata.RunTool(Path.Combine(toolsDirectory, "ffprobe.exe"), new string[] {
                "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name", "-of", "default=noprint_wrappers=1:nokey=1", input
            }, job, shouldStop);
            return json.Trim();
        }

        private static bool IsCopyCompatible(AudioFormatOption format, string codec)
        {
            if (format.Id == "best") return true;
            foreach (string accepted in format.CopyCodecs.Split(','))
                if (String.Equals(accepted, codec, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void AddQualityArguments(List<string> args, AudioFormatOption format, AppSettings settings)
        {
            if (settings.QualityMode == "bitrate" && format.HasBitrate)
            {
                args.Add("-b:a"); args.Add(settings.BitrateKbps.ToString() + "k");
                return;
            }
            if (settings.QualityMode != "variable") return;
            double q = settings.VariableQuality / 10.0;
            if (format.Id == "mp3")
            {
                args.Add("-q:a"); args.Add(settings.VariableQuality.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (format.Id == "aac" || format.Id == "m4a")
            {
                args.Add("-q:a"); args.Add((4.0 - 3.9 * q).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (format.Id == "vorbis")
            {
                args.Add("-q:a"); args.Add((10 - settings.VariableQuality).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
